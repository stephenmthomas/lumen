using System.IO;
using System.Text;
using System.Linq;
using DisplayControl.Native;

namespace DisplayControl.Services;

public class ICCProfileService
{
    private static readonly string ColorDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        @"spool\drivers\color");

    /// <summary>
    /// Gets all ICC profiles currently associated with a monitor device.
    /// </summary>
    public List<string> GetProfilesForMonitor(string deviceName)
    {
        var profiles = new List<string>();

        // Set up ENUMTYPE to query by device name
        var enumType = new NativeMethods.ENUMTYPE
        {
            dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ENUMTYPE>(),
            dwVersion = 0x0300, // ENUM_TYPE_VERSION
            dwFields = NativeMethods.ET_DEVICENAME,
            pDeviceName = deviceName
        };

        uint bufferSize = 0;
        uint profileCount = 0;

        // First call: get required buffer size
        NativeMethods.EnumColorProfiles(null, ref enumType, null, ref bufferSize, ref profileCount);

        if (bufferSize > 0)
        {
            byte[] buffer = new byte[bufferSize];

            // Second call: get the actual profiles
            if (NativeMethods.EnumColorProfiles(null, ref enumType, buffer, ref bufferSize, ref profileCount))
            {
                // Parse multi-string buffer (Unicode, double-null terminated)
                string allProfiles = Encoding.Unicode.GetString(buffer);
                profiles.AddRange(allProfiles.Split('\0', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return profiles;
    }

    /// <summary>
    /// Gets all available *.icc files in the clor-drivers folder.
    /// </summary>
    public List<string> GetAllAvailableProfiles()
    {
        var colorDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"spool\drivers\color");

        if (!Directory.Exists(colorDir))
            return new List<string>();

        // Just list .icc and .icm files directly (much faster than EnumColorProfiles)
        return Directory.GetFiles(colorDir, "*.ic?")
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Cast<string>()
            .OrderBy(f => f)
            .ToList();
    }


    /// <summary>
    /// Gets all available ICC profiles suitable for display devices.
    /// </summary>
    public List<string> EnumAllDisplayProfiles()
    {
        var profiles = new List<string>();

        // Set up ENUMTYPE to filter for display profiles only
        var enumType = new NativeMethods.ENUMTYPE
        {
            dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ENUMTYPE>(),
            dwVersion = 0x0300,
            dwFields = NativeMethods.ET_CLASS, // Filter by profile class
            dwClass = 0x6D6E7472 // 'mntr' = monitor/display profile class
        };

        uint bufferSize = 0;
        uint profileCount = 0;

        // First call: get buffer size
        NativeMethods.EnumColorProfiles(null, ref enumType, null, ref bufferSize, ref profileCount);

        if (bufferSize > 0)
        {
            byte[] buffer = new byte[bufferSize];

            if (NativeMethods.EnumColorProfiles(null, ref enumType, buffer, ref bufferSize, ref profileCount))
            {
                string allProfiles = Encoding.Unicode.GetString(buffer);
                profiles.AddRange(allProfiles.Split('\0', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return profiles.OrderBy(p => p).ToList();
    }

    /// <summary>
    /// Gets all available profiles from system, including ones not needed for Monitors. Useless for us.
    /// </summary>
    public List<string> GetAllAvailableDeviceProfiles()
    {
        var profiles = new List<string>();

        // Set up ENUMTYPE to get all profiles (no device filter)
        var enumType = new NativeMethods.ENUMTYPE
        {
            dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ENUMTYPE>(),
            dwVersion = 0x0300,
            dwFields = 0 // Match all
        };

        uint bufferSize = 0;
        uint profileCount = 0;

        // First call: get buffer size
        NativeMethods.EnumColorProfiles(null, ref enumType, null, ref bufferSize, ref profileCount);

        if (bufferSize > 0)
        {
            byte[] buffer = new byte[bufferSize];

            if (NativeMethods.EnumColorProfiles(null, ref enumType, buffer, ref bufferSize, ref profileCount))
            {
                string allProfiles = Encoding.Unicode.GetString(buffer);
                profiles.AddRange(allProfiles.Split('\0', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return profiles.OrderBy(p => p).ToList();
    }

    /// <summary>
    /// Sets the default ICC profile for a monitor using the modern WCS API.
    /// </summary>
    public bool SetProfileForMonitor(string deviceName, string profileName)
    {
        try
        {
            return NativeMethods.WcsSetDefaultColorProfile(
                NativeMethods.WCS_PROFILE_MANAGEMENT_SCOPE.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                deviceName,
                NativeMethods.COLORPROFILETYPE.CPT_ICC,
                NativeMethods.COLORPROFILESUBTYPE.CPST_NONE,
                0,
                profileName
            );
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Associates a profile with a device (makes it available for selection).
    /// </summary>
    public bool AssociateProfile(string deviceName, string profileName)
    {
        return NativeMethods.AssociateColorProfileWithDevice(null, profileName, deviceName);
    }

    /// <summary>
    /// Opens the system color profile directory in Windows Explorer.
    /// </summary>
    public static void OpenColorProfileDirectory()
    {
        var colorDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"spool\drivers\color");

        if (Directory.Exists(colorDir))
        {
            System.Diagnostics.Process.Start("explorer.exe", colorDir);
        }
    }

    /// <summary>
    /// Opens Windows Display Settings.
    /// </summary>
    public static void OpenDisplaySettings()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:display",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Fallback to older control panel method
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "desk.cpl",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
    }

    /// <summary>
    /// Opens Windows Color Management control panel.
    /// </summary>
    public static void OpenColorManagement()
    {
        System.Diagnostics.Process.Start("colorcpl.exe");
    }




}