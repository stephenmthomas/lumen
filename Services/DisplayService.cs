using DisplayControl.Native;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using static DisplayControl.SettingsWindow;

namespace DisplayControl.Services;

/// <summary>
/// High-level service for manipulating display color properties.
/// Wraps the native Windows APIs - both GDI and DXGI complete, however,
/// DXGI implementation is waiting on DX "full screen" emulation.
/// </summary>
public class DisplayService : IDisposable
{
    public SettingsWindow? SettingsWindow { get; set; }

    private readonly Dictionary<string, IntPtr> _monitorHandles = new();
    private readonly Dictionary<string, NativeMethods.GammaRamp> _originalRamps = new();
    private bool _disposed;

    private DxgiDisplayBackend? _dxgiBackend;
    private DisplayBackend _activeBackend = DisplayBackend.GDI;

    public DisplayService()
    {
        EnumerateMonitors();

        // Try to initialize DXGI backend (available on Windows 7+)
        try
        {
            _dxgiBackend = new DxgiDisplayBackend();
            if (!_dxgiBackend.Initialize())
            {
                _dxgiBackend.Dispose();
                _dxgiBackend = null;
            }
        }
        catch
        {
            _dxgiBackend?.Dispose();
            _dxgiBackend = null;
        }
    }


    #region Monitor Enumeration

    public List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var info = new NativeMethods.MONITORINFOEX();
                info.Size = Marshal.SizeOf(info);
                
                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    monitors.Add(new MonitorInfo
                    {
                        DeviceName = info.DeviceName,
                        IsPrimary = (info.Flags & 1) != 0,
                        Bounds = new Rect(
                            info.Monitor.Left,
                            info.Monitor.Top,
                            info.Monitor.Right - info.Monitor.Left,
                            info.Monitor.Bottom - info.Monitor.Top)
                    });
                }
                
                return true;
            }, IntPtr.Zero);

        return monitors;
    }

    private void EnumerateMonitors()
    {
        var monitors = GetMonitors();
        
        foreach (var monitor in monitors)
        {
            var hdc = NativeMethods.CreateDC("DISPLAY", monitor.DeviceName, null, IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                _monitorHandles[monitor.DeviceName] = hdc;
                
                var ramp = new NativeMethods.GammaRamp();
                if (NativeMethods.GetDeviceGammaRamp(hdc, ref ramp))
                {
                    _originalRamps[monitor.DeviceName] = ramp;
                }
            }
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Gets the current gamma ramp for a specific monitor.
    /// Returns null if monitor not found.
    /// </summary>
    public NativeMethods.GammaRamp? GetCurrentGammaRamp(string deviceName)
    {
        if (!_monitorHandles.TryGetValue(deviceName, out var hdc))
            return null;

        var ramp = new NativeMethods.GammaRamp();
        if (NativeMethods.GetDeviceGammaRamp(hdc, ref ramp))
            return ramp;

        return null;
    }

    /// <summary>
    /// Gets the original (startup) gamma ramp for a specific monitor.
    /// This is what was active when DisplayService initialized.
    /// </summary>
    public NativeMethods.GammaRamp? GetOriginalGammaRamp(string deviceName)
    {
        return _originalRamps.TryGetValue(deviceName, out var ramp) ? ramp : null;
    }

    /// <summary>
    /// Checks if the current gamma ramp differs from the original (identity) ramp.
    /// Returns true if something has modified the gamma since boot/service init.
    /// </summary>
    public bool IsGammaRampModified(string deviceName)
    {
        var current = GetCurrentGammaRamp(deviceName);
        var original = GetOriginalGammaRamp(deviceName);

        if (current == null || original == null)
            return false;

        // Compare a few key points (checking all 768 values is overkill)
        for (int i = 0; i < 256; i += 32) // Sample every 32nd value
        {
            if (current.Value.Red[i] != original.Value.Red[i] ||
                current.Value.Green[i] != original.Value.Green[i] ||
                current.Value.Blue[i] != original.Value.Blue[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a monitor's gamma ramp is identity (linear 1:1 mapping).
    /// </summary>
    public bool DeviceHasIdentityRamp(string deviceName)
    {
        var ramp = GetCurrentGammaRamp(deviceName);
        if (ramp == null)
            return false;

        return IsIdentityRamp(ramp.Value);
    }

    /// <summary>
    /// Analyzes a gamma ramp to detect if it's identity (linear 1:1 mapping).
    /// </summary>
    public static bool IsIdentityRamp(NativeMethods.GammaRamp ramp)
    {
        for (int i = 0; i < 256; i++)
        {
            ushort expected = (ushort)(i * 256);

            // Allow small tolerance for rounding
            if (Math.Abs(ramp.Red[i] - expected) > 10 ||
                Math.Abs(ramp.Green[i] - expected) > 10 ||
                Math.Abs(ramp.Blue[i] - expected) > 10)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsValidGammaRamp(NativeMethods.GammaRamp ramp)
    {
        // Check monotonicity (each value >= previous)
        for (int i = 1; i < 256; i++)
        {
            if (ramp.Red[i] < ramp.Red[i - 1]) return false;
            if (ramp.Green[i] < ramp.Green[i - 1]) return false;
            if (ramp.Blue[i] < ramp.Blue[i - 1]) return false;
        }

        // Check range (0-65535)
        // Already guaranteed by ushort, but could add min/max spread checks

        return true;
    }

    private bool RampsMatch(NativeMethods.GammaRamp a, NativeMethods.GammaRamp b)
    {
        for (int i = 0; i < 256; i++)
        {
            if (Math.Abs(a.Red[i] - b.Red[i]) > 10) return false;
            if (Math.Abs(a.Green[i] - b.Green[i]) > 10) return false;
            if (Math.Abs(a.Blue[i] - b.Blue[i]) > 10) return false;
        }
        return true;
    }

    /// <summary>
    /// Gets a summary of the gamma ramp state for display in UI.
    /// </summary>
    public string GetGammaRampSummary(string deviceName)
    {
        var current = GetCurrentGammaRamp(deviceName);
        var original = GetOriginalGammaRamp(deviceName);

        if (current == null)
            return "Ramp is null!";

        if (original == null)
            return "No baseline ramp.";

        bool isModified = IsGammaRampModified(deviceName);
        bool isIdentity = IsIdentityRamp(current.Value);

        // Build status string with all applicable flags
        var flags = new List<string>();

        if (isIdentity)
            flags.Add("Baseline Ramp");
        else
            flags.Add("Modified Ramp");

        if (!isModified)
            flags.Add("App Default");
        else
            flags.Add("App Modified");

        return string.Join(" | ", flags);
    }

    public void SetBrightness(double brightness)
    {
        foreach (var kvp in _monitorHandles)
            SetBrightnessForMonitor(kvp.Key, brightness);
    }

    public void SetBrightnessForMonitor(string deviceName, double brightness)
    {
        if (!_monitorHandles.TryGetValue(deviceName, out var hdc)) return;
        var ramp = CreateBrightnessRamp(brightness);
        NativeMethods.SetDeviceGammaRamp(hdc, ref ramp);
    }

    public void SetContrast(double contrast)
    {
        foreach (var kvp in _monitorHandles)
            SetContrastForMonitor(kvp.Key, contrast);
    }

    public void SetContrastForMonitor(string deviceName, double contrast)
    {
        if (!_monitorHandles.TryGetValue(deviceName, out var hdc)) return;
        var ramp = CreateContrastRamp(contrast);
        NativeMethods.SetDeviceGammaRamp(hdc, ref ramp);
    }

    public void SetGamma(double gamma)
    {
        foreach (var kvp in _monitorHandles)
            SetGammaForMonitor(kvp.Key, gamma);
    }

    public void SetGammaForMonitor(string deviceName, double gamma)
    {
        if (!_monitorHandles.TryGetValue(deviceName, out var hdc)) return;
        var ramp = CreateGammaRamp(gamma);
        NativeMethods.SetDeviceGammaRamp(hdc, ref ramp);
    }

    public void SetColorTemperature(int kelvin)
    {
        foreach (var kvp in _monitorHandles)
            SetColorTemperatureForMonitor(kvp.Key, kelvin);
    }

    public void SetColorTemperatureForMonitor(string deviceName, int kelvin)
    {
        if (!_monitorHandles.TryGetValue(deviceName, out var hdc)) return;
        var ramp = CreateColorTemperatureRamp(kelvin);
        NativeMethods.SetDeviceGammaRamp(hdc, ref ramp);
    }

    public void ApplyColorProfile(ColorProfile profile)
    {
        var ramp = CreateFullRamp(profile);

        if (_activeBackend == DisplayBackend.DXGI && _dxgiBackend != null)
        {
            System.Diagnostics.Debug.WriteLine($"DXGI: Applying to ALL, available outputs: {string.Join(", ", _dxgiBackend.GetAvailableOutputs())}");
            bool result = _dxgiBackend.ApplyGammaRampAll(ramp);
            System.Diagnostics.Debug.WriteLine($"DXGI: ApplyGammaRampAll result = {result}");
        }
        else
        {
            foreach (var kvp in _monitorHandles)
            {
                NativeMethods.SetDeviceGammaRamp(kvp.Value, ref ramp);
            }
        }
    }

    public void ApplyColorProfileToMonitor(string deviceName, ColorProfile profile)
    {
        var ramp = CreateFullRamp(profile);

        // Validate BEFORE sending
        if (!IsValidGammaRamp(ramp))
        {
            SettingsWindow.UpdateStatus("Invalid Gamma Ramp", StatusType.Error);
            return;
        }

        if (_activeBackend == DisplayBackend.DXGI && _dxgiBackend != null)
        {
            System.Diagnostics.Debug.WriteLine($"DXGI: Applying to '{deviceName}', available outputs: {string.Join(", ", _dxgiBackend.GetAvailableOutputs())}");
            bool result = _dxgiBackend.ApplyGammaRamp(deviceName, ramp);
            System.Diagnostics.Debug.WriteLine($"DXGI: ApplyGammaRamp result = {result}");
        }
        else
        {
            if (_monitorHandles.TryGetValue(deviceName, out var hdc))
            {
                bool success = NativeMethods.SetDeviceGammaRamp(hdc, ref ramp);

                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    SettingsWindow.UpdateStatus("Ramp failure!", StatusType.Error);

                    System.Diagnostics.Debug.WriteLine($"SetDeviceGammaRamp FAILED: Error {error}");
                    return;
                }
            }
        }
    }

    public void ResetAll()
    {
        if (_activeBackend == DisplayBackend.DXGI && _dxgiBackend != null)
        {
            _dxgiBackend.ResetAll();
        }
        else
        {
            ResetAllGdi();
        }
    }

    private void ResetAllGdi()
    {
        foreach (var kvp in _monitorHandles)
        {
            if (_originalRamps.TryGetValue(kvp.Key, out var originalRamp))
                NativeMethods.SetDeviceGammaRamp(kvp.Value, ref originalRamp);
        }
    }

    public void ResetMonitor(string deviceName)
    {
        if (_activeBackend == DisplayBackend.DXGI && _dxgiBackend != null)
        {
            _dxgiBackend.Reset(deviceName);
        }
        else
        {
            if (_monitorHandles.TryGetValue(deviceName, out var hdc) &&
                _originalRamps.TryGetValue(deviceName, out var originalRamp))
            {
                NativeMethods.SetDeviceGammaRamp(hdc, ref originalRamp);
            }
        }
    }

    #endregion

    #region DXGI Methods
    /// <summary>
    /// Gets/sets the active display backend (GDI or DXGI).
    /// </summary>
    public DisplayBackend ActiveBackend
    {
        get => _activeBackend;
        set
        {
            if (value == DisplayBackend.DXGI && _dxgiBackend == null)
                return; // DXGI not available, stay on GDI

            // Reset current backend before switching
            if (_activeBackend == DisplayBackend.GDI)
                ResetAllGdi();
            else
                _dxgiBackend?.ResetAll();

            _activeBackend = value;
        }
    }

    /// <summary>
    /// Returns true if DXGI backend is available on this system.
    /// </summary>
    public bool IsDxgiAvailable => _dxgiBackend?.IsAvailable ?? false;

    /// <summary>
    /// Gets adapter description for a monitor (only available with DXGI).
    /// </summary>
    public string GetAdapterDescription(string deviceName)
    {
        return _dxgiBackend?.GetAdapterDescription(deviceName) ?? "Unknown (DXGI not available)";
    }
    #endregion

    #region Master Ramp Builder

    /// <summary>
    /// Builds the complete gamma ramp from all profile parameters.
    /// Pipeline: Input -> Levels -> Shadows/Mids/Highlights/Whites/Blacks -> 
    ///           Exposure/Offset -> Contrast -> Gamma -> Master Curve ->
    ///           Per-channel RGB Curves -> Color Temp + Tint + RGB Gains ->
    ///           Saturation/Vibrance -> Dynamic Enhancers -> Clamp
    /// </summary>
    private NativeMethods.GammaRamp CreateFullRamp(ColorProfile p)
    {
        var ramp = new NativeMethods.GammaRamp();

        // Pre-compute color temperature multipliers
        double temp = p.ColorTemperature / 100.0;
        double rMult = CalculateRedMultiplier(temp) * p.RedGain;
        double gMult = CalculateGreenMultiplier(temp) * p.GreenGain;
        double bMult = CalculateBlueMultiplier(temp) * p.BlueGain;

        // Tint: shift green-magenta axis
        // Positive tint = more green, negative = more magenta (boost red+blue)
        if (Math.Abs(p.Tint) > 0.001)
        {
            double tintFactor = p.Tint; // -1.0 to 1.0
            gMult *= (1.0 + tintFactor * 0.3);
            rMult *= (1.0 - tintFactor * 0.15);
            bMult *= (1.0 - tintFactor * 0.15);
        }

        // Pre-compute master curve LUT if enabled
        ushort[]? masterLUT = p.UseCurve ? p.Curve.GenerateLUT() : null;

        // Pre-compute per-channel curve LUTs if enabled
        ushort[]? redCurveLUT = p.UseChannelCurves && p.RedCurve != null ? p.RedCurve.GenerateLUT() : null;
        ushort[]? greenCurveLUT = p.UseChannelCurves && p.GreenCurve != null ? p.GreenCurve.GenerateLUT() : null;
        ushort[]? blueCurveLUT = p.UseChannelCurves && p.BlueCurve != null ? p.BlueCurve.GenerateLUT() : null;

        // Pre-compute blue light filter
        double blueFilterR = 1.0, blueFilterG = 1.0, blueFilterB = 1.0;
        if (p.BlueLightFilter > 0.001)
        {
            blueFilterG = 1.0 - (p.BlueLightFilter * 0.2);
            blueFilterB = 1.0 - p.BlueLightFilter;
        }

        for (int i = 0; i < 256; i++)
        {
            double val = i / 255.0; // normalize to 0-1

            // === LEVELS ===
            if (p.UseLevels)
            {
                val = p.Levels.MapNormalized(val);
            }

            // === ZONE-BASED ADJUSTMENTS (Shadows/Midtones/Highlights/Whites/Blacks) ===
            val = ApplyZoneAdjustments(val, p);

            // === EXPOSURE (stops, like photography) ===
            // Exposure is a multiplicative gain in linear light
            if (Math.Abs(p.Exposure) > 0.001)
            {
                val *= Math.Pow(2.0, p.Exposure);
            }

            // === OFFSET (additive shift) ===
            if (Math.Abs(p.Offset) > 0.001)
            {
                val += p.Offset;
            }

            // === CONTRAST ===
            val = (val - 0.5) * p.Contrast + 0.5;

            // === GAMMA CORRECTION ===
            val = Math.Clamp(val, 0.0, 1.0);
            if (Math.Abs(p.Gamma - 1.0) > 0.001)
            {
                val = Math.Pow(val, 1.0 / p.Gamma);
            }

            // === MASTER CURVE ===
            if (masterLUT != null)
            {
                // Curve LUT is 0-65535; map our 0-1 value through it
                int idx = (int)Math.Clamp(val * 255, 0, 255);
                val = masterLUT[idx] / 65535.0;
            }

            // === DYNAMIC CONTRAST ===
            if (p.DynamicContrast > 0.001)
            {
                val = ApplyDynamicContrast(val, p.DynamicContrast);
            }

            // Clamp before channel split
            val = Math.Clamp(val, 0.0, 1.0);

            // === SPLIT TO RGB CHANNELS ===
            double r = val;
            double g = val;
            double b = val;

            // === PER-CHANNEL RGB CURVES ===
            if (redCurveLUT != null)
            {
                int idx = (int)Math.Clamp(r * 255, 0, 255);
                r = redCurveLUT[idx] / 65535.0;
            }
            if (greenCurveLUT != null)
            {
                int idx = (int)Math.Clamp(g * 255, 0, 255);
                g = greenCurveLUT[idx] / 65535.0;
            }
            if (blueCurveLUT != null)
            {
                int idx = (int)Math.Clamp(b * 255, 0, 255);
                b = blueCurveLUT[idx] / 65535.0;
            }

            // === COLOR TEMPERATURE + TINT + RGB GAINS ===
            r *= rMult / 255.0;
            g *= gMult / 255.0;
            b *= bMult / 255.0;

            // === BLUE LIGHT FILTER ===
            if (p.BlueLightFilter > 0.001)
            {
                r *= blueFilterR;
                g *= blueFilterG;
                b *= blueFilterB;
            }

            // === VIBRANCE (selective saturation) ===
            if (Math.Abs(p.Vibrance) > 0.001)
            {
                double avg = (r + g + b) / 3.0;
                double maxC = Math.Max(r, Math.Max(g, b));
                double minC = Math.Min(r, Math.Min(g, b));
                double currentSat = (maxC > 0.001) ? (maxC - minC) / maxC : 0;
                
                // Less saturated pixels get more boost
                double vibranceMult = 1.0 + p.Vibrance * (1.0 - currentSat);
                r = avg + (r - avg) * vibranceMult;
                g = avg + (g - avg) * vibranceMult;
                b = avg + (b - avg) * vibranceMult;
            }

            // === SATURATION ===
            if (Math.Abs(p.Saturation - 1.0) > 0.001)
            {
                // Luminance-weighted average for perceptual accuracy
                double luma = r * 0.2126 + g * 0.7152 + b * 0.0722;
                r = luma + (r - luma) * p.Saturation;
                g = luma + (g - luma) * p.Saturation;
                b = luma + (b - luma) * p.Saturation;
            }

            // === BLACK EQUALIZER (lift shadow detail without affecting highlights) ===
            if (Math.Abs(p.BlackEqualizer) > 0.001)
            {
                double lift = p.BlackEqualizer * 0.15;
                r = ApplyBlackEqualizer(r, lift);
                g = ApplyBlackEqualizer(g, lift);
                b = ApplyBlackEqualizer(b, lift);
            }

            // === WHITE EQUALIZER (compress highlights for detail recovery) ===
            if (Math.Abs(p.WhiteEqualizer) > 0.001)
            {
                double compress = p.WhiteEqualizer;
                r = ApplyWhiteEqualizer(r, compress);
                g = ApplyWhiteEqualizer(g, compress);
                b = ApplyWhiteEqualizer(b, compress);
            }

            // === BRIGHTNESS (final multiplier) ===
            r *= p.Brightness;
            g *= p.Brightness;
            b *= p.Brightness;

            // === OUTPUT (0-65535) ===
            ramp.Red[i] = (ushort)Math.Clamp(r * 65535, 0, 65535);
            ramp.Green[i] = (ushort)Math.Clamp(g * 65535, 0, 65535);
            ramp.Blue[i] = (ushort)Math.Clamp(b * 65535, 0, 65535);
        }

        SettingsWindow?.UpdateGammaRampControl(ramp);

        return ramp;
    }

    #endregion

    #region Zone-Based Adjustments

    /// <summary>
    /// Applies shadows/midtones/highlights/whites/blacks adjustments.
    /// Each zone targets a specific luminance range with smooth falloff.
    /// </summary>
    private double ApplyZoneAdjustments(double val, ColorProfile p)
    {
        double result = val;

        // Blacks: affects the very bottom of the tonal range (0-0.15)
        if (Math.Abs(p.Blacks) > 0.001)
        {
            double weight = SmoothFalloff(val, 0.0, 0.15);
            result += p.Blacks * 0.3 * weight;
        }

        // Shadows: affects lower range (0.05-0.35)
        if (Math.Abs(p.Shadows) > 0.001)
        {
            double weight = SmoothFalloff(val, 0.15, 0.25);
            result += p.Shadows * 0.25 * weight;
        }

        // Midtones: affects middle range (0.25-0.75)
        if (Math.Abs(p.Midtones) > 0.001)
        {
            double weight = SmoothFalloff(val, 0.5, 0.3);
            result += p.Midtones * 0.2 * weight;
        }

        // Highlights: affects upper range (0.65-0.95)
        if (Math.Abs(p.Highlights) > 0.001)
        {
            double weight = SmoothFalloff(val, 0.85, 0.25);
            result += p.Highlights * 0.25 * weight;
        }

        // Whites: affects the very top of the tonal range (0.85-1.0)
        if (Math.Abs(p.Whites) > 0.001)
        {
            double weight = SmoothFalloff(val, 1.0, 0.15);
            result += p.Whites * 0.3 * weight;
        }

        return Math.Clamp(result, 0.0, 1.0);
    }

    /// <summary>
    /// Gaussian-like smooth falloff for zone targeting.
    /// </summary>
    private double SmoothFalloff(double value, double center, double width)
    {
        double dist = (value - center) / width;
        return Math.Exp(-0.5 * dist * dist);
    }

    #endregion

    #region Dynamic Enhancers

    private double ApplyDynamicContrast(double val, double strength)
    {
        // S-curve contrast enhancement
        // Stronger in midtones, gentler at extremes
        double x = val;
        double sCurve = x * x * (3.0 - 2.0 * x); // Hermite smoothstep
        return val + (sCurve - val) * strength;
    }

    private double ApplyBlackEqualizer(double val, double lift)
    {
        // Lift shadows: add brightness to dark areas, fade to zero effect at white
        double shadowMask = 1.0 - val; // strongest effect on darks
        shadowMask = shadowMask * shadowMask; // square for smoother falloff
        return val + lift * shadowMask;
    }

    private double ApplyWhiteEqualizer(double val, double compress)
    {
        // Compress highlights: reduce brightness of bright areas
        if (val > 0.5)
        {
            double highlightMask = (val - 0.5) * 2.0; // 0 at 0.5, 1 at 1.0
            highlightMask = highlightMask * highlightMask;
            return val - compress * 0.2 * highlightMask;
        }
        return val;
    }

    #endregion

    #region Simple Ramp Creators (for backward compat)

    private NativeMethods.GammaRamp CreateBrightnessRamp(double brightness)
    {
        var ramp = new NativeMethods.GammaRamp();
        for (int i = 0; i < 256; i++)
        {
            int value = (int)(i * 256 * brightness);
            value = Math.Clamp(value, 0, 65535);
            ramp.Red[i] = (ushort)value;
            ramp.Green[i] = (ushort)value;
            ramp.Blue[i] = (ushort)value;
        }
        return ramp;
    }

    private NativeMethods.GammaRamp CreateContrastRamp(double contrast)
    {
        var ramp = new NativeMethods.GammaRamp();
        for (int i = 0; i < 256; i++)
        {
            int value = (int)(((i - 128) * contrast + 128) * 256);
            value = Math.Clamp(value, 0, 65535);
            ramp.Red[i] = (ushort)value;
            ramp.Green[i] = (ushort)value;
            ramp.Blue[i] = (ushort)value;
        }
        return ramp;
    }

    private NativeMethods.GammaRamp CreateGammaRamp(double gamma)
    {
        var ramp = new NativeMethods.GammaRamp();
        for (int i = 0; i < 256; i++)
        {
            double normalized = i / 255.0;
            double corrected = Math.Pow(normalized, 1.0 / gamma);
            int value = (int)(corrected * 65535);
            value = Math.Clamp(value, 0, 65535);
            ramp.Red[i] = (ushort)value;
            ramp.Green[i] = (ushort)value;
            ramp.Blue[i] = (ushort)value;
        }
        return ramp;
    }

    private NativeMethods.GammaRamp CreateColorTemperatureRamp(int kelvin)
    {
        var ramp = new NativeMethods.GammaRamp();
        double temp = kelvin / 100.0;
        double red = CalculateRedMultiplier(temp);
        double green = CalculateGreenMultiplier(temp);
        double blue = CalculateBlueMultiplier(temp);

        for (int i = 0; i < 256; i++)
        {
            ramp.Red[i] = (ushort)Math.Clamp((i * 256 * red / 255), 0, 65535);
            ramp.Green[i] = (ushort)Math.Clamp((i * 256 * green / 255), 0, 65535);
            ramp.Blue[i] = (ushort)Math.Clamp((i * 256 * blue / 255), 0, 65535);
        }
        return ramp;
    }

    #endregion

    #region Color Temperature Helpers

    private double CalculateRedMultiplier(double temp)
    {
        if (temp <= 66) return 255;
        double red = temp - 60;
        red = 329.698727446 * Math.Pow(red, -0.1332047592);
        return Math.Clamp(red, 0, 255);
    }

    private double CalculateGreenMultiplier(double temp)
    {
        if (temp <= 66)
        {
            double green = 99.4708025861 * Math.Log(temp) - 161.1195681661;
            return Math.Clamp(green, 0, 255);
        }
        else
        {
            double green = temp - 60;
            green = 288.1221695283 * Math.Pow(green, -0.0755148492);
            return Math.Clamp(green, 0, 255);
        }
    }

    private double CalculateBlueMultiplier(double temp)
    {
        if (temp >= 66) return 255;
        if (temp <= 19) return 0;
        double blue = temp - 10;
        blue = 138.5177312231 * Math.Log(blue) - 305.0447927307;
        return Math.Clamp(blue, 0, 255);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        ResetAll();

        _dxgiBackend?.Dispose();
        _dxgiBackend = null;

        foreach (var hdc in _monitorHandles.Values)
            NativeMethods.DeleteDC(hdc);
        _monitorHandles.Clear();
        _originalRamps.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~DisplayService() { Dispose(); }

    #endregion
}

#region Data Models

public class MonitorInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public Rect Bounds { get; set; }
}

/// <summary>
/// Complete color profile for display manipulation.
/// </summary>
public class ColorProfile
{
    // === BASIC / EXPOSURE ===
    public double Brightness { get; set; } = 1.0;         // 0.0 to 2.0
    public double Contrast { get; set; } = 1.0;           // 0.0 to 2.0
    public double Gamma { get; set; } = 1.0;              // 0.1 to 3.0
    public double Exposure { get; set; } = 0.0;           // -3.0 to +3.0 stops
    public double Offset { get; set; } = 0.0;             // -0.5 to +0.5

    // === TONAL ZONES ===
    public double Shadows { get; set; } = 0.0;            // -1.0 to 1.0
    public double Midtones { get; set; } = 0.0;           // -1.0 to 1.0
    public double Highlights { get; set; } = 0.0;         // -1.0 to 1.0
    public double Whites { get; set; } = 0.0;             // -1.0 to 1.0
    public double Blacks { get; set; } = 0.0;             // -1.0 to 1.0

    // === COLOR ===
    public int ColorTemperature { get; set; } = 6500;     // 2000 to 10000 Kelvin
    public double Tint { get; set; } = 0.0;               // -1.0 (magenta) to +1.0 (green)
    public double RedGain { get; set; } = 1.0;            // 0.0 to 2.0
    public double GreenGain { get; set; } = 1.0;          // 0.0 to 2.0
    public double BlueGain { get; set; } = 1.0;           // 0.0 to 2.0
    public double Saturation { get; set; } = 1.0;         // 0.0 to 3.0
    public double Vibrance { get; set; } = 0.0;           // -1.0 to 1.0

    // === DYNAMIC ENHANCERS ===
    public double DynamicContrast { get; set; } = 0.0;    // 0.0 to 1.0
    public double BlueLightFilter { get; set; } = 0.0;    // 0.0 to 1.0
    public double BlackEqualizer { get; set; } = 0.0;     // -1.0 to 1.0
    public double WhiteEqualizer { get; set; } = 0.0;     // 0.0 to 1.0

    // === CURVES ===
    public ToneCurve Curve { get; set; } = new();          // Master luminance curve
    public ToneCurve RedCurve { get; set; } = new();       // Per-channel red
    public ToneCurve GreenCurve { get; set; } = new();     // Per-channel green
    public ToneCurve BlueCurve { get; set; } = new();      // Per-channel blue
    public bool UseCurve { get; set; } = false;
    public bool UseChannelCurves { get; set; } = false;

    // === LEVELS ===
    public LevelsControl Levels { get; set; } = new();
    public bool UseLevels { get; set; } = false;

    public string Name { get; set; } = "Default";

    public ColorProfile Clone()
    {
        return new ColorProfile
        {
            Brightness = Brightness,
            Contrast = Contrast,
            Gamma = Gamma,
            Exposure = Exposure,
            Offset = Offset,
            Shadows = Shadows,
            Midtones = Midtones,
            Highlights = Highlights,
            Whites = Whites,
            Blacks = Blacks,
            ColorTemperature = ColorTemperature,
            Tint = Tint,
            RedGain = RedGain,
            GreenGain = GreenGain,
            BlueGain = BlueGain,
            Saturation = Saturation,
            Vibrance = Vibrance,
            DynamicContrast = DynamicContrast,
            BlueLightFilter = BlueLightFilter,
            BlackEqualizer = BlackEqualizer,
            WhiteEqualizer = WhiteEqualizer,
            Curve = Curve.Clone(),
            RedCurve = RedCurve.Clone(),
            GreenCurve = GreenCurve.Clone(),
            BlueCurve = BlueCurve.Clone(),
            UseCurve = UseCurve,
            UseChannelCurves = UseChannelCurves,
            Levels = Levels.Clone(),
            UseLevels = UseLevels,
            Name = Name
        };
    }

    public static ColorProfile Default => new();

    public static ColorProfile Night => new()
    {
        Brightness = 0.7,
        Contrast = 1.1,
        Gamma = 1.1,
        ColorTemperature = 3400,
        Saturation = 0.9,
        BlueLightFilter = 0.5,
        Name = "Night Mode"
    };

    public static ColorProfile Reading => new()
    {
        Brightness = 0.8,
        Contrast = 1.2,
        Gamma = 1.0,
        ColorTemperature = 4500,
        Saturation = 0.85,
        BlueLightFilter = 0.3,
        Name = "Reading"
    };

    public static ColorProfile Gaming => new()
    {
        Brightness = 1.2,
        Contrast = 1.3,
        Gamma = 0.9,
        ColorTemperature = 6500,
        Saturation = 1.3,
        Vibrance = 0.3,
        DynamicContrast = 0.2,
        BlackEqualizer = 0.3,
        Name = "Gaming"
    };
}

/// <summary>
/// Tone curve with control points and cubic spline interpolation.
/// </summary>
public class ToneCurve
{
    public List<CurvePoint> Points { get; set; } = new()
    {
        new CurvePoint(0, 0),
        new CurvePoint(255, 255)
    };

    public ToneCurve Clone()
    {
        return new ToneCurve
        {
            Points = Points.Select(p => new CurvePoint(p.Input, p.Output)).ToList()
        };
    }

    /// <summary>
    /// Returns true if this curve is the identity (no effect).
    /// </summary>
    public bool IsIdentity()
    {
        if (Points.Count != 2) return false;
        var sorted = Points.OrderBy(p => p.Input).ToList();
        return sorted[0].Input == 0 && sorted[0].Output == 0 &&
               sorted[1].Input == 255 && sorted[1].Output == 255;
    }

    public ushort[] GenerateLUT()
    {
        var lut = new ushort[256];
        var sorted = Points.OrderBy(p => p.Input).ToList();

        if (sorted.Count < 2)
        {
            // Fallback: identity
            for (int i = 0; i < 256; i++)
                lut[i] = (ushort)(i * 256);
            return lut;
        }

        // Use monotone cubic interpolation for smooth curves without overshooting
        var spline = new MonotoneCubicSpline(sorted);

        for (int i = 0; i < 256; i++)
        {
            double output = spline.Evaluate(i);
            lut[i] = (ushort)Math.Clamp(output * 256, 0, 65535);
        }

        return lut;
    }
}

/// <summary>
/// Monotone cubic Hermite spline - prevents overshooting between control points.
/// Much better than linear interpolation for smooth curve editing.
/// </summary>
public class MonotoneCubicSpline
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _m; // tangents

    public MonotoneCubicSpline(List<CurvePoint> points)
    {
        int n = points.Count;
        _x = new double[n];
        _y = new double[n];
        _m = new double[n];

        for (int i = 0; i < n; i++)
        {
            _x[i] = points[i].Input;
            _y[i] = points[i].Output;
        }

        if (n < 2) return;

        // Compute secants
        var d = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            double dx = _x[i + 1] - _x[i];
            d[i] = dx > 0 ? (_y[i + 1] - _y[i]) / dx : 0;
        }

        // Initialize tangents
        _m[0] = d[0];
        for (int i = 1; i < n - 1; i++)
        {
            _m[i] = (d[i - 1] + d[i]) / 2.0;
        }
        _m[n - 1] = d[n - 2];

        // Enforce monotonicity (Fritsch-Carlson)
        for (int i = 0; i < n - 1; i++)
        {
            if (Math.Abs(d[i]) < 1e-10)
            {
                _m[i] = 0;
                _m[i + 1] = 0;
            }
            else
            {
                double a = _m[i] / d[i];
                double b = _m[i + 1] / d[i];
                double h = Math.Sqrt(a * a + b * b);
                if (h > 3)
                {
                    double t = 3.0 / h;
                    _m[i] = t * a * d[i];
                    _m[i + 1] = t * b * d[i];
                }
            }
        }
    }

    public double Evaluate(double x)
    {
        int n = _x.Length;
        if (n == 0) return x;
        if (x <= _x[0]) return _y[0];
        if (x >= _x[n - 1]) return _y[n - 1];

        // Find segment
        int seg = 0;
        for (int i = 0; i < n - 1; i++)
        {
            if (x >= _x[i] && x <= _x[i + 1])
            {
                seg = i;
                break;
            }
        }

        double dx = _x[seg + 1] - _x[seg];
        if (dx <= 0) return _y[seg];

        double t = (x - _x[seg]) / dx;
        double t2 = t * t;
        double t3 = t2 * t;

        // Cubic Hermite basis
        double h00 = 2 * t3 - 3 * t2 + 1;
        double h10 = t3 - 2 * t2 + t;
        double h01 = -2 * t3 + 3 * t2;
        double h11 = t3 - t2;

        return h00 * _y[seg] + h10 * dx * _m[seg] +
               h01 * _y[seg + 1] + h11 * dx * _m[seg + 1];
    }
}

public class CurvePoint
{
    public int Input { get; set; }
    public int Output { get; set; }

    public CurvePoint(int input, int output)
    {
        Input = input;
        Output = output;
    }
}

/// <summary>
/// Levels control (input/output range mapping).
/// </summary>
public class LevelsControl
{
    public byte InputBlack { get; set; } = 0;
    public byte InputWhite { get; set; } = 255;
    public byte OutputBlack { get; set; } = 0;
    public byte OutputWhite { get; set; } = 255;

    public LevelsControl Clone()
    {
        return new LevelsControl
        {
            InputBlack = InputBlack,
            InputWhite = InputWhite,
            OutputBlack = OutputBlack,
            OutputWhite = OutputWhite
        };
    }

    public int Map(int input)
    {
        if (input <= InputBlack) return OutputBlack;
        if (input >= InputWhite) return OutputWhite;
        double normalized = (input - InputBlack) / (double)(InputWhite - InputBlack);
        return (int)(OutputBlack + normalized * (OutputWhite - OutputBlack));
    }

    public double MapNormalized(double val)
    {
        double inBlack = InputBlack / 255.0;
        double inWhite = InputWhite / 255.0;
        double outBlack = OutputBlack / 255.0;
        double outWhite = OutputWhite / 255.0;

        if (val <= inBlack) return outBlack;
        if (val >= inWhite) return outWhite;

        double normalized = (val - inBlack) / (inWhite - inBlack);
        return outBlack + normalized * (outWhite - outBlack);
    }
}

#endregion
