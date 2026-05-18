using System.Runtime.InteropServices;
using System.Text;

namespace DisplayControl.Native;

/// <summary>
/// Native Windows API declarations for advanced display color manipulation.
/// Uses built-in Windows APIs for gamma ramp, ICC profiles, and monitor enumeration.
/// </summary>
public static class NativeMethods
{
    #region Constants
    
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x80000;
    public const int LWA_ALPHA = 0x2;
    
    // Monitor enumeration
    public const int MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const int MONITOR_DEFAULTTONEAREST = 0x00000002;
    
    // Color profile constants
    public const int CLASS_MONITOR = 0x00000001;
    public const int CLASS_PRINTER = 0x00000002;
    public const int CLASS_SCANNER = 0x00000004;
    
    #endregion

    #region Structures
    
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GammaRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;

        public GammaRamp()
        {
            Red = new ushort[256];
            Green = new ushort[256];
            Blue = new ushort[256];
        }
    }

    #endregion

    #region GDI32 - Gamma Ramp Control
    
    /// <summary>
    /// Sets the gamma ramp for the specified device context.
    /// This is the core API for brightness/contrast/gamma control.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern bool SetDeviceGammaRamp(IntPtr hdc, ref GammaRamp lpRamp);

    /// <summary>
    /// Gets the current gamma ramp from the device context.
    /// Useful for restoring original values or reading current state.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern bool GetDeviceGammaRamp(IntPtr hdc, ref GammaRamp lpRamp);

    /// <summary>
    /// Creates a device context for the specified device (monitor).
    /// Pass null for device name to get DC for entire virtual screen.
    /// </summary>
    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, 
        string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    #endregion

    #region User32 - Device Context & Window Management
    
    /// <summary>
    /// Gets device context for a window (or entire screen if hWnd is IntPtr.Zero).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>
    /// Enumerate all display monitors.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, 
        ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    /// <summary>
    /// Register a system-wide hotkey that works even when app doesn't have focus.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    #endregion

    #region DXGI - DirectX Graphics Infrastructure (Modern Gamma Control)

    /// <summary>
    /// Creates a DXGI factory. This is the entry point for all DXGI operations.
    /// </summary>
    [DllImport("dxgi.dll", SetLastError = true)]
    public static extern int CreateDXGIFactory(
        ref Guid riid,
        out IntPtr ppFactory);

    // DXGI IIDs (Interface Identifiers)
    public static Guid IID_IDXGIFactory = new("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
    public static Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    /// <summary>
    /// DXGI_GAMMA_CONTROL structure.
    /// Scale and Offset are applied as: output = clamp(Scale * curveValue + Offset)
    /// GammaCurve has 1025 entries (vs 256 for GDI gamma ramp).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_GAMMA_CONTROL
    {
        public DXGI_RGB Scale;
        public DXGI_RGB Offset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1025)]
        public DXGI_RGB[] GammaCurve;

        public DXGI_GAMMA_CONTROL()
        {
            Scale = new DXGI_RGB { Red = 1.0f, Green = 1.0f, Blue = 1.0f };
            Offset = new DXGI_RGB { Red = 0.0f, Green = 0.0f, Blue = 0.0f };
            GammaCurve = new DXGI_RGB[1025];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_RGB
    {
        public float Red;
        public float Green;
        public float Blue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_GAMMA_CONTROL_CAPABILITIES
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool ScaleAndOffsetSupported;
        public float MaxConvertedValue;
        public float MinConvertedValue;
        public uint NumGammaControlPoints;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1025)]
        public float[] ControlPointPositions;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] DeviceName;
        public int DesktopLeft;
        public int DesktopTop;
        public int DesktopRight;
        public int DesktopBottom;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public IntPtr DedicatedVideoMemory;
        public IntPtr DedicatedSystemMemory;
        public IntPtr SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
    }

    // =====================================================================
    // COM Interface VTable Offsets
    // DXGI uses COM, so we call methods via vtable function pointers.
    // IUnknown: [0]=QueryInterface, [1]=AddRef, [2]=Release
    // IDXGIFactory: IUnknown + [3..6] = DXGI Object methods, [7]=EnumAdapters
    // IDXGIAdapter: IUnknown + [3..6], [7]=EnumOutputs, [8]=GetDesc
    // IDXGIOutput:  IUnknown + [3..6], [7]=GetDesc, ..., [21]=SetGammaControl,
    //               [22]=GetGammaControl, [23]=GetGammaControlCapabilities
    // =====================================================================

    // IDXGIFactory vtable
    public const int DXGI_FACTORY_ENUM_ADAPTERS = 7;

    // IDXGIAdapter vtable
    public const int DXGI_ADAPTER_ENUM_OUTPUTS = 7;
    public const int DXGI_ADAPTER_GET_DESC = 8;

    // IDXGIOutput vtable
    public const int DXGI_OUTPUT_GET_DESC = 7;
    public const int DXGI_OUTPUT_SET_GAMMA_CONTROL = 14;
    public const int DXGI_OUTPUT_GET_GAMMA_CONTROL = 15;
    public const int DXGI_OUTPUT_GET_GAMMA_CONTROL_CAPABILITIES = 16;

    /// <summary>
    /// Helper to call a COM method via vtable offset.
    /// </summary>
    public static IntPtr GetVTableMethod(IntPtr comObject, int slot)
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        return Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
    }

    // COM Release helper
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint ReleaseDelegate(IntPtr self);

    // IDXGIFactory::EnumAdapters(UINT Adapter, IDXGIAdapter** ppAdapter)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int EnumAdaptersDelegate(IntPtr self, uint adapter, out IntPtr ppAdapter);

    // IDXGIAdapter::EnumOutputs(UINT Output, IDXGIOutput** ppOutput)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int EnumOutputsDelegate(IntPtr self, uint output, out IntPtr ppOutput);

    // IDXGIAdapter::GetDesc(DXGI_ADAPTER_DESC* pDesc)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetAdapterDescDelegate(IntPtr self, out DXGI_ADAPTER_DESC pDesc);

    // IDXGIOutput::GetDesc(DXGI_OUTPUT_DESC* pDesc)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetOutputDescDelegate(IntPtr self, out DXGI_OUTPUT_DESC pDesc);

    // IDXGIOutput::SetGammaControl(const DXGI_GAMMA_CONTROL* pArray)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetGammaControlDelegate(IntPtr self, ref DXGI_GAMMA_CONTROL pArray);

    // IDXGIOutput::GetGammaControl(DXGI_GAMMA_CONTROL* pArray)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetGammaControlDelegate(IntPtr self, out DXGI_GAMMA_CONTROL pArray);

    // IDXGIOutput::GetGammaControlCapabilities(DXGI_GAMMA_CONTROL_CAPABILITIES* pGammaCaps)
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetGammaControlCapabilitiesDelegate(IntPtr self, out DXGI_GAMMA_CONTROL_CAPABILITIES pGammaCaps);

    #endregion

    #region ICM32 - ICC Color Profile Management

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool AssociateColorProfileWithDevice(
        string pMachineName,
        string pProfileName,
        string pDeviceName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool DisassociateColorProfileFromDevice(
        string pMachineName,
        string pProfileName,
        string pDeviceName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool EnumColorProfiles(
        string pMachineName,
        ref ENUMTYPE pEnumRecord,
        byte[] pBuffer,
        ref uint pdwSize,
        ref uint pnProfiles);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ENUMTYPE
    {
        public uint dwSize;
        public uint dwVersion;
        public uint dwFields;
        public string pDeviceName;
        public uint dwMediaType;
        public uint dwDitheringMode;
        public uint dwResolution0;
        public uint dwResolution1;
        public uint dwCMMType;
        public uint dwClass;
        public uint dwDataColorSpace;
        public uint dwConnectionSpace;
        public uint dwSignature;
        public uint dwPlatform;
        public uint dwProfileFlags;
        public uint dwManufacturer;
        public uint dwModel;
        public uint dwAttributes0;
        public uint dwAttributes1;
        public uint dwRenderingIntent;
        public uint dwCreator;
    }

    // Flags for ENUMTYPE.dwFields
    public const uint ET_DEVICENAME = 0x00000001;
    public const uint ET_MEDIATYPE = 0x00000002;
    public const uint ET_DITHERMODE = 0x00000004;
    public const uint ET_RESOLUTION = 0x00000008;
    public const uint ET_CMMTYPE = 0x00000010;
    public const uint ET_CLASS = 0x00000020;
    public const uint ET_DATACOLORSPACE = 0x00000040;
    public const uint ET_CONNECTIONSPACE = 0x00000080;
    public const uint ET_SIGNATURE = 0x00000100;
    public const uint ET_PLATFORM = 0x00000200;
    public const uint ET_PROFILEFLAGS = 0x00000400;
    public const uint ET_MANUFACTURER = 0x00000800;
    public const uint ET_MODEL = 0x00001000;
    public const uint ET_ATTRIBUTES = 0x00002000;
    public const uint ET_RENDERINGINTENT = 0x00004000;
    public const uint ET_CREATOR = 0x00008000;

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool WcsSetDefaultColorProfile(
        WCS_PROFILE_MANAGEMENT_SCOPE scope,
        string pDeviceName,
        COLORPROFILETYPE cptColorProfileType,
        COLORPROFILESUBTYPE cpstColorProfileSubType,
        uint dwProfileID,
        string pProfileName);

    public enum WCS_PROFILE_MANAGEMENT_SCOPE
    {
        WCS_PROFILE_MANAGEMENT_SCOPE_SYSTEM_WIDE = 0,
        WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER = 1
    }

    public enum COLORPROFILETYPE
    {
        CPT_ICC = 0,
        CPT_DMP = 1,
        CPT_CAMP = 2,
        CPT_GMMP = 3
    }

    public enum COLORPROFILESUBTYPE
    {
        CPST_NONE = 4
    }

    #endregion

    #region Hotkey Modifiers

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;
    
    #endregion

    #region Virtual Key Codes
    
    public const uint VK_F1 = 0x70;
    public const uint VK_F2 = 0x71;
    public const uint VK_F3 = 0x72;
    public const uint VK_F4 = 0x73;
    public const uint VK_F5 = 0x74;
    public const uint VK_F6 = 0x75;
    public const uint VK_F7 = 0x76;
    public const uint VK_F8 = 0x77;
    public const uint VK_F9 = 0x78;
    public const uint VK_F10 = 0x79;
    public const uint VK_F11 = 0x7A;
    public const uint VK_F12 = 0x7B;
    
    public const uint VK_UP = 0x26;
    public const uint VK_DOWN = 0x28;
    public const uint VK_LEFT = 0x25;
    public const uint VK_RIGHT = 0x27;
    
    public const uint VK_OEM_PLUS = 0xBB;   // '=' key
    public const uint VK_OEM_MINUS = 0xBD;  // '-' key
    
    #endregion
}
