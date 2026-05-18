using System.Runtime.InteropServices;
using DisplayControl.Native;

namespace DisplayControl.Services;

/// <summary>
/// DXGI-based display backend using IDXGIOutput::SetGammaControl.
/// This is the modern replacement for GDI SetDeviceGammaRamp.
/// 
/// Key differences from GDI:
///  - 1025 control points instead of 256 (better precision)
///  - Separate Scale and Offset fields (hardware-accelerated brightness/contrast)
///  - Works through the graphics driver, not legacy GDI path
///  - Better multi-monitor isolation
///  - Proper interaction with DWM composition
///  
/// The color math pipeline (CreateFullRamp) stays in DisplayService.
/// This class only handles the DXGI enumeration and gamma control calls.
/// </summary>
public class DxgiDisplayBackend : IDisposable
{
    private IntPtr _factory = IntPtr.Zero;
    private readonly Dictionary<string, DxgiOutputInfo> _outputs = new();
    private readonly Dictionary<string, NativeMethods.DXGI_GAMMA_CONTROL> _originalGamma = new();
    private bool _initialized;
    private bool _disposed;

    private class DxgiOutputInfo
    {
        public IntPtr Adapter;
        public IntPtr Output;
        public string DeviceName = "";
        public string AdapterDescription = "";
    }

    public bool IsAvailable => _initialized;

    /// <summary>
    /// Initialize the DXGI backend. Call this once on startup.
    /// Returns true if DXGI is available and outputs were found.
    /// </summary>
    public bool Initialize()
    {
        try
        {
            var iid = NativeMethods.IID_IDXGIFactory1;
            int hr = NativeMethods.CreateDXGIFactory(ref iid, out _factory);
            
            if (hr != 0 || _factory == IntPtr.Zero)
            {
                // Try older factory
                iid = NativeMethods.IID_IDXGIFactory;
                hr = NativeMethods.CreateDXGIFactory(ref iid, out _factory);
                
                if (hr != 0 || _factory == IntPtr.Zero)
                    return false;
            }

            EnumerateOutputs();
            _initialized = _outputs.Count > 0;
            return _initialized;
        }
        catch
        {
            _initialized = false;
            return false;
        }
    }

    /// <summary>
    /// Enumerates all DXGI adapters and their outputs.
    /// Maps output DeviceName to the corresponding IDXGIOutput pointer.
    /// </summary>
    private void EnumerateOutputs()
    {
        NativeMethods.EnumAdaptersDelegate enumAdapters;
        try
        {
            enumAdapters = Marshal.GetDelegateForFunctionPointer<NativeMethods.EnumAdaptersDelegate>(
                NativeMethods.GetVTableMethod(_factory, NativeMethods.DXGI_FACTORY_ENUM_ADAPTERS));
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine("DXGI: Failed to get EnumAdapters");
            return;
        }

        uint adapterIndex = 0;
        while (true)
        {
            IntPtr adapter;
            try
            {
                int hr = enumAdapters(_factory, adapterIndex, out adapter);
                if (hr != 0 || adapter == IntPtr.Zero)
                    break;
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"DXGI: EnumAdapters crashed at index {adapterIndex}");
                break;
            }

            System.Diagnostics.Debug.WriteLine($"DXGI: Found adapter {adapterIndex}");
            System.Diagnostics.Debug.WriteLine($"DXGI: Skipping adapter desc, moving to outputs");

            // Enumerate outputs
            NativeMethods.EnumOutputsDelegate enumOutputs;
            try
            {
                enumOutputs = Marshal.GetDelegateForFunctionPointer<NativeMethods.EnumOutputsDelegate>(
                    NativeMethods.GetVTableMethod(adapter, NativeMethods.DXGI_ADAPTER_ENUM_OUTPUTS));
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("DXGI: Failed to get EnumOutputs");
                adapterIndex++;
                continue;
            }

            uint outputIndex = 0;
            while (true)
            {
                IntPtr output;
                try
                {
                    int hr = enumOutputs(adapter, outputIndex, out output);
                    if (hr != 0 || output == IntPtr.Zero)
                        break;
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"DXGI: EnumOutputs crashed at index {outputIndex}");
                    break;
                }

                System.Diagnostics.Debug.WriteLine($"DXGI: Found output {outputIndex}");

                try
                {
                    var getOutputDesc = Marshal.GetDelegateForFunctionPointer<NativeMethods.GetOutputDescDelegate>(
                        NativeMethods.GetVTableMethod(output, NativeMethods.DXGI_OUTPUT_GET_DESC));
                    getOutputDesc(output, out var outputDesc);

                    string deviceName = new string(outputDesc.DeviceName).TrimEnd('\0');
                    System.Diagnostics.Debug.WriteLine($"DXGI: Output = {deviceName}, Attached = {outputDesc.AttachedToDesktop}");

                    if (outputDesc.AttachedToDesktop)
                    {
                        _outputs[deviceName] = new DxgiOutputInfo
                        {
                            Adapter = adapter,
                            Output = output,
                            DeviceName = deviceName,
                            AdapterDescription = "Unknown"
                        };

                        System.Diagnostics.Debug.WriteLine($"DXGI: Registered {deviceName}");
                    }
                    else
                    {
                        ReleaseComObject(output);
                    }
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"DXGI: GetOutputDesc crashed at output {outputIndex}");
                    ReleaseComObject(output);
                }

                outputIndex++;
            }

            adapterIndex++;
        }

        System.Diagnostics.Debug.WriteLine($"DXGI: Enumeration complete, {_outputs.Count} outputs found");
    }

    private void SaveOriginalGamma(IntPtr output, string deviceName)
    {
        try
        {
            var getGamma = Marshal.GetDelegateForFunctionPointer<NativeMethods.GetGammaControlDelegate>(
                NativeMethods.GetVTableMethod(output, NativeMethods.DXGI_OUTPUT_GET_GAMMA_CONTROL));

            int hr = getGamma(output, out var gamma);
            if (hr == 0)
            {
                _originalGamma[deviceName] = gamma;
            }
        }
        catch
        {
            // Some outputs may not support gamma control
        }
    }

    #region Public API (mirrors DisplayService GDI methods)

    /// <summary>
    /// Gets a list of device names that have DXGI outputs available.
    /// These should match the GDI device names from EnumDisplayMonitors.
    /// </summary>
    public List<string> GetAvailableOutputs()
    {
        return _outputs.Keys.ToList();
    }

    /// <summary>
    /// Gets adapter info for a specific output (GPU name, etc).
    /// </summary>
    public string GetAdapterDescription(string deviceName)
    {
        return _outputs.TryGetValue(deviceName, out var info) 
            ? info.AdapterDescription 
            : "Unknown";
    }

    /// <summary>
    /// Applies a 256-entry GDI-style gamma ramp via DXGI.
    /// Interpolates the 256 entries up to 1025 for DXGI's higher precision.
    /// This is the main bridge method — takes the same ramp your GDI path produces.
    /// </summary>
    public bool ApplyGammaRamp(string deviceName, NativeMethods.GammaRamp ramp)
    {
        if (!_outputs.TryGetValue(deviceName, out var info))
            return false;

        var dxgiGamma = ConvertGdiRampToDxgi(ramp);
        return SetGammaControl(info.Output, ref dxgiGamma);
    }

    /// <summary>
    /// Applies a 256-entry gamma ramp to all outputs.
    /// </summary>
    public bool ApplyGammaRampAll(NativeMethods.GammaRamp ramp)
    {
        var dxgiGamma = ConvertGdiRampToDxgi(ramp);
        bool allSuccess = true;

        foreach (var info in _outputs.Values)
        {
            if (!SetGammaControl(info.Output, ref dxgiGamma))
                allSuccess = false;
        }

        return allSuccess;
    }

    /// <summary>
    /// Applies a native 1025-entry DXGI gamma control directly.
    /// Use this when you want the full precision of DXGI.
    /// </summary>
    public bool ApplyDxgiGamma(string deviceName, NativeMethods.DXGI_GAMMA_CONTROL gamma)
    {
        if (!_outputs.TryGetValue(deviceName, out var info))
            return false;

        return SetGammaControl(info.Output, ref gamma);
    }

    /// <summary>
    /// Resets a specific output to its original gamma.
    /// </summary>
    public void Reset(string deviceName)
    {
        if (_outputs.TryGetValue(deviceName, out var info) &&
            _originalGamma.TryGetValue(deviceName, out var original))
        {
            SetGammaControl(info.Output, ref original);
        }
    }

    /// <summary>
    /// Resets all outputs to their original gamma.
    /// </summary>
    public void ResetAll()
    {
        foreach (var deviceName in _outputs.Keys)
        {
            Reset(deviceName);
        }
    }

    /// <summary>
    /// Gets the gamma control capabilities for a specific output.
    /// Useful for checking how many control points are actually supported.
    /// </summary>
    public NativeMethods.DXGI_GAMMA_CONTROL_CAPABILITIES? GetCapabilities(string deviceName)
    {
        if (!_outputs.TryGetValue(deviceName, out var info))
            return null;

        try
        {
            var getCaps = Marshal.GetDelegateForFunctionPointer<NativeMethods.GetGammaControlCapabilitiesDelegate>(
                NativeMethods.GetVTableMethod(info.Output, NativeMethods.DXGI_OUTPUT_GET_GAMMA_CONTROL_CAPABILITIES));

            int hr = getCaps(info.Output, out var caps);
            return hr == 0 ? caps : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Conversion (GDI 256 → DXGI 1025)

    /// <summary>
    /// Converts a GDI 256-entry gamma ramp to a DXGI 1025-entry gamma control.
    /// Uses cubic interpolation for smooth upsampling.
    /// </summary>
    private static NativeMethods.DXGI_GAMMA_CONTROL ConvertGdiRampToDxgi(NativeMethods.GammaRamp ramp)
    {
        var dxgi = new NativeMethods.DXGI_GAMMA_CONTROL();

        // Scale and Offset at identity — all adjustments baked into the curve
        dxgi.Scale = new NativeMethods.DXGI_RGB { Red = 1.0f, Green = 1.0f, Blue = 1.0f };
        dxgi.Offset = new NativeMethods.DXGI_RGB { Red = 0.0f, Green = 0.0f, Blue = 0.0f };

        // Normalize GDI ramp values from 0-65535 to 0.0-1.0
        float[] gdiRed = new float[256];
        float[] gdiGreen = new float[256];
        float[] gdiBlue = new float[256];

        for (int i = 0; i < 256; i++)
        {
            gdiRed[i] = ramp.Red[i] / 65535f;
            gdiGreen[i] = ramp.Green[i] / 65535f;
            gdiBlue[i] = ramp.Blue[i] / 65535f;
        }

        // Interpolate 256 → 1025
        for (int i = 0; i < 1025; i++)
        {
            // Map 0..1024 to 0..255 in the source ramp
            float srcPos = i * 255f / 1024f;
            int idx0 = (int)srcPos;
            int idx1 = Math.Min(idx0 + 1, 255);
            float frac = srcPos - idx0;

            // Cubic interpolation for smoother curves
            int idxM1 = Math.Max(idx0 - 1, 0);
            int idx2 = Math.Min(idx1 + 1, 255);

            dxgi.GammaCurve[i] = new NativeMethods.DXGI_RGB
            {
                Red = CubicInterpolate(gdiRed[idxM1], gdiRed[idx0], gdiRed[idx1], gdiRed[idx2], frac),
                Green = CubicInterpolate(gdiGreen[idxM1], gdiGreen[idx0], gdiGreen[idx1], gdiGreen[idx2], frac),
                Blue = CubicInterpolate(gdiBlue[idxM1], gdiBlue[idx0], gdiBlue[idx1], gdiBlue[idx2], frac)
            };
        }

        return dxgi;
    }

    /// <summary>
    /// Catmull-Rom cubic interpolation for smooth upsampling.
    /// </summary>
    private static float CubicInterpolate(float y0, float y1, float y2, float y3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float a = -0.5f * y0 + 1.5f * y1 - 1.5f * y2 + 0.5f * y3;
        float b = y0 - 2.5f * y1 + 2.0f * y2 - 0.5f * y3;
        float c = -0.5f * y0 + 0.5f * y2;
        float d = y1;

        float result = a * t3 + b * t2 + c * t + d;
        return Math.Clamp(result, 0f, 1f);
    }

    #endregion

    #region DXGI COM Calls

    private static bool SetGammaControl(IntPtr output, ref NativeMethods.DXGI_GAMMA_CONTROL gamma)
    {
        try
        {
            var setGamma = Marshal.GetDelegateForFunctionPointer<NativeMethods.SetGammaControlDelegate>(
                NativeMethods.GetVTableMethod(output, NativeMethods.DXGI_OUTPUT_SET_GAMMA_CONTROL));

            int hr = setGamma(output, ref gamma);
            System.Diagnostics.Debug.WriteLine($"DXGI: SetGammaControl HRESULT = 0x{hr:X8}");
            return hr == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DXGI: SetGammaControl exception: {ex.Message}");
            return false;
        }
    }

    private static void ReleaseComObject(IntPtr comObject)
    {
        if (comObject != IntPtr.Zero)
        {
            var release = Marshal.GetDelegateForFunctionPointer<NativeMethods.ReleaseDelegate>(
                NativeMethods.GetVTableMethod(comObject, 2)); // IUnknown::Release is always slot 2
            release(comObject);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        // Restore original gamma on all outputs
        ResetAll();

        // Release all COM objects
        foreach (var info in _outputs.Values)
        {
            ReleaseComObject(info.Output);
            // Note: don't release adapters here — they may be shared across outputs
        }
        _outputs.Clear();
        _originalGamma.Clear();

        if (_factory != IntPtr.Zero)
        {
            ReleaseComObject(_factory);
            _factory = IntPtr.Zero;
        }

        _initialized = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~DxgiDisplayBackend() { Dispose(); }

    #endregion
}

/// <summary>
/// Enum for selecting which backend DisplayService uses.
/// </summary>
public enum DisplayBackend
{
    /// <summary>GDI SetDeviceGammaRamp — legacy, works everywhere</summary>
    GDI,
    /// <summary>DXGI IDXGIOutput::SetGammaControl — modern, 1025 control points</summary>
    DXGI
}
