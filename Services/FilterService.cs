using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace DisplayControl.Services;

/// <summary>
/// Applies fullscreen color transformation filters using the Windows Magnification API.
/// This operates at the DWM composition level — after gamma ramps, before final output.
/// Only one fullscreen color effect can be active system-wide at a time.
/// </summary>
public class FilterService : IDisposable
{
    private bool _initialized;
    private bool _disposed;
    private bool _isActive;

    // Current filter state
    private FilterProfile _currentProfile = new();

    public FilterService()
    {
        _initialized = MagInitialize();
    }

    public bool IsAvailable => _initialized;
    public bool IsActive => _isActive;

    #region Public API

    /// <summary>
    /// Applies the current filter profile as a fullscreen color effect.
    /// </summary>
    public bool ApplyFilter(FilterProfile profile)
    {
        if (!_initialized) return false;

        _currentProfile = profile;
        var matrix = profile.BuildMatrix();

        bool success = MagSetFullscreenColorEffect(ref matrix);

        _isActive = success;
        return success;
    }

    /// <summary>
    /// Applies a preset filter by type.
    /// </summary>
    public bool ApplyPreset(FilterPreset preset)
    {
        var profile = FilterProfile.FromPreset(preset);
        return ApplyFilter(profile);
    }

    /// <summary>
    /// Removes all filters (applies identity matrix).
    /// </summary>
    public bool ClearFilter()
    {
        if (!_initialized) return false;

        var identity = ColorMatrix.Identity;
        bool success = MagSetFullscreenColorEffect(ref identity);
        _isActive = false;
        _currentProfile = new FilterProfile();
        return success;
    }

    /// <summary>
    /// Checks if Windows Accessibility color filters are currently active.
    /// </summary>
    public static bool AreSystemFiltersActive()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\ColorFiltering");
            if (key != null)
            {
                var active = key.GetValue("Active");
                return active is int val && val == 1;
            }
        }
        catch { }
        return false;
    }

    #endregion

    #region P/Invoke - Magnification API

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagSetFullscreenColorEffect(ref ColorMatrix matrix);

    [DllImport("Magnification.dll", SetLastError = true)]
    private static extern bool MagGetFullscreenColorEffect(ref ColorMatrix matrix);

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        if (_isActive)
        {
            ClearFilter();
        }

        if (_initialized)
        {
            MagUninitialize();
            _initialized = false;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~FilterService() { Dispose(); }

    #endregion
}

#region Color Matrix

/// <summary>
/// 5x5 color transformation matrix for the Magnification API.
/// Layout: [R_out] = [row0] * [R_in, G_in, B_in, A_in, 1.0]
///         [G_out] = [row1] * [R_in, G_in, B_in, A_in, 1.0]
///         [B_out] = [row2] * [R_in, G_in, B_in, A_in, 1.0]
///         [A_out] = [row3] * [R_in, G_in, B_in, A_in, 1.0]
///         (row4 is unused but must be present)
/// 
/// Values are 0.0-1.0 normalized. Identity has 1s on diagonal.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ColorMatrix
{
    // Row 0: Red output
    public float M00, M01, M02, M03, M04;
    // Row 1: Green output
    public float M10, M11, M12, M13, M14;
    // Row 2: Blue output
    public float M20, M21, M22, M23, M24;
    // Row 3: Alpha output
    public float M30, M31, M32, M33, M34;
    // Row 4: unused (must be 0,0,0,0,1 or all zeros)
    public float M40, M41, M42, M43, M44;

    /// <summary>
    /// Identity matrix — no color transformation.
    /// </summary>
    public static ColorMatrix Identity => new()
    {
        M00 = 1,
        M01 = 0,
        M02 = 0,
        M03 = 0,
        M04 = 0,
        M10 = 0,
        M11 = 1,
        M12 = 0,
        M13 = 0,
        M14 = 0,
        M20 = 0,
        M21 = 0,
        M22 = 1,
        M23 = 0,
        M24 = 0,
        M30 = 0,
        M31 = 0,
        M32 = 0,
        M33 = 1,
        M34 = 0,
        M40 = 0,
        M41 = 0,
        M42 = 0,
        M43 = 0,
        M44 = 1,
    };

    /// <summary>
    /// Access matrix element by row and column index.
    /// </summary>
    public float this[int row, int col]
    {
        get => (row, col) switch
        {
            (0, 0) => M00,
            (0, 1) => M01,
            (0, 2) => M02,
            (0, 3) => M03,
            (0, 4) => M04,
            (1, 0) => M10,
            (1, 1) => M11,
            (1, 2) => M12,
            (1, 3) => M13,
            (1, 4) => M14,
            (2, 0) => M20,
            (2, 1) => M21,
            (2, 2) => M22,
            (2, 3) => M23,
            (2, 4) => M24,
            (3, 0) => M30,
            (3, 1) => M31,
            (3, 2) => M32,
            (3, 3) => M33,
            (3, 4) => M34,
            (4, 0) => M40,
            (4, 1) => M41,
            (4, 2) => M42,
            (4, 3) => M43,
            (4, 4) => M44,
            _ => throw new IndexOutOfRangeException()
        };
        set
        {
            switch ((row, col))
            {
                case (0, 0): M00 = value; break;
                case (0, 1): M01 = value; break;
                case (0, 2): M02 = value; break;
                case (0, 3): M03 = value; break;
                case (0, 4): M04 = value; break;
                case (1, 0): M10 = value; break;
                case (1, 1): M11 = value; break;
                case (1, 2): M12 = value; break;
                case (1, 3): M13 = value; break;
                case (1, 4): M14 = value; break;
                case (2, 0): M20 = value; break;
                case (2, 1): M21 = value; break;
                case (2, 2): M22 = value; break;
                case (2, 3): M23 = value; break;
                case (2, 4): M24 = value; break;
                case (3, 0): M30 = value; break;
                case (3, 1): M31 = value; break;
                case (3, 2): M32 = value; break;
                case (3, 3): M33 = value; break;
                case (3, 4): M34 = value; break;
                case (4, 0): M40 = value; break;
                case (4, 1): M41 = value; break;
                case (4, 2): M42 = value; break;
                case (4, 3): M43 = value; break;
                case (4, 4): M44 = value; break;
                default: throw new IndexOutOfRangeException();
            }
        }
    }

    /// <summary>
    /// Multiply two color matrices together. Order matters: result = a * b
    /// This composes two transformations (b is applied first, then a).
    /// </summary>
    public static ColorMatrix Multiply(ColorMatrix a, ColorMatrix b)
    {

        var result = new ColorMatrix();
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 5; col++)
            {
                float sum = 0;
                for (int k = 0; k < 5; k++)
                {
                    sum += a[row, k] * b[k, col];
                }
                result[row, col] = sum;
            }
        }
        return result;
    }

    /// <summary>
    /// Linearly interpolate between identity and this matrix.
    /// strength=0 returns identity, strength=1 returns this matrix.
    /// </summary>
    public ColorMatrix WithStrength(float strength)
    {
        var id = Identity;
        var result = new ColorMatrix();
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 5; col++)
            {
                result[row, col] = id[row, col] + (this[row, col] - id[row, col]) * strength;
            }
        }
        return result;
    }
}

#endregion

#region Filter Matrices

/// <summary>
/// Factory methods for common color transformation matrices.
/// All matrices are designed to be composed via multiplication.
/// </summary>
public static class FilterMatrices
{
    // Rec.709 luminance coefficients
    //https://en.wikipedia.org/wiki/Rec._709

    private const float LR = 0.2126f;
    private const float LG = 0.7152f;
    private const float LB = 0.0722f;

    /// <summary>
    /// Grayscale using perceptual luminance weights (Rec.709).
    /// </summary>
    public static ColorMatrix Grayscale() => new()
    {
        M00 = LR,
        M01 = LR,
        M02 = LR,
        M03 = 0,
        M04 = 0,
        M10 = LG,
        M11 = LG,
        M12 = LG,
        M13 = 0,
        M14 = 0,
        M20 = LB,
        M21 = LB,
        M22 = LB,
        M23 = 0,
        M24 = 0,
        M30 = 0,
        M31 = 0,
        M32 = 0,
        M33 = 1,
        M34 = 0,
        M40 = 0,
        M41 = 0,
        M42 = 0,
        M43 = 0,
        M44 = 1,
    };

    /// <summary>
    /// Color inversion.
    /// </summary>
    public static ColorMatrix Invert() => new()
    {
        M00 = -1,
        M01 = 0,
        M02 = 0,
        M03 = 0,
        M04 = 0,
        M10 = 0,
        M11 = -1,
        M12 = 0,
        M13 = 0,
        M14 = 0,
        M20 = 0,
        M21 = 0,
        M22 = -1,
        M23 = 0,
        M24 = 0,
        M30 = 0,
        M31 = 0,
        M32 = 0,
        M33 = 1,
        M34 = 0,
        M40 = 1,
        M41 = 1,
        M42 = 1,
        M43 = 0,
        M44 = 1,
    };

    /// <summary>
    /// Sepia tone effect.
    /// </summary>
    public static ColorMatrix Sepia() => new()
    {
        M00 = 0.393f,
        M01 = 0.349f,
        M02 = 0.272f,
        M03 = 0,
        M04 = 0,
        M10 = 0.769f,
        M11 = 0.686f,
        M12 = 0.534f,
        M13 = 0,
        M14 = 0,
        M20 = 0.189f,
        M21 = 0.168f,
        M22 = 0.131f,
        M23 = 0,
        M24 = 0,
        M30 = 0,
        M31 = 0,
        M32 = 0,
        M33 = 1,
        M34 = 0,
        M40 = 0,
        M41 = 0,
        M42 = 0,
        M43 = 0,
        M44 = 1,
    };

    /// <summary>
    /// Saturation adjustment. 0 = grayscale, 1 = identity, >1 = oversaturated.
    /// Cross-channel operation — fundamentally different from gamma-ramp saturation.
    /// </summary>
    public static ColorMatrix Saturation(float sat)
    {
        float sr = (1 - sat) * LR;
        float sg = (1 - sat) * LG;
        float sb = (1 - sat) * LB;

        return new ColorMatrix
        {
            M00 = sr + sat,
            M01 = sr,
            M02 = sr,
            M03 = 0,
            M04 = 0,
            M10 = sg,
            M11 = sg + sat,
            M12 = sg,
            M13 = 0,
            M14 = 0,
            M20 = sb,
            M21 = sb,
            M22 = sb + sat,
            M23 = 0,
            M24 = 0,
            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = 0,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Brightness offset. Adds a constant to all color channels.
    /// Range: -1.0 to +1.0
    /// </summary>
    public static ColorMatrix Brightness(float brightness)
    {
        float b = brightness;

        return new ColorMatrix
        {
            M00 = 1,
            M01 = 0,
            M02 = 0,
            M03 = 0,
            M04 = 0,
            M10 = 0,
            M11 = 1,
            M12 = 0,
            M13 = 0,
            M14 = 0,
            M20 = 0,
            M21 = 0,
            M22 = 1,
            M23 = 0,
            M24 = 0,
            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = b,
            M41 = b,
            M42 = b,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Contrast scaling around midpoint (0.5).
    /// Range: 0.0 (flat gray) to 2.0+ (high contrast)
    /// </summary>
    public static ColorMatrix Contrast(float contrast)
    {
        float offset = 0.5f * (1 - contrast);

        return new ColorMatrix
        {
            M00 = contrast,
            M01 = 0,
            M02 = 0,
            M03 = 0,
            M04 = 0,
            M10 = 0,
            M11 = contrast,
            M12 = 0,
            M13 = 0,
            M14 = 0,
            M20 = 0,
            M21 = 0,
            M22 = contrast,
            M23 = 0,
            M24 = 0,
            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = offset,
            M41 = offset,
            M42 = offset,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Hue rotation in degrees. Rotates colors around the luminance axis.
    /// This is impossible to do with gamma ramps — it's a true cross-channel operation.
    /// </summary>
    public static ColorMatrix HueRotation(float degrees)
    {
        float rad = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);

        return new ColorMatrix
        {
            M00 = LR + cos * (1 - LR) + sin * (-LR),
            M01 = LR + cos * (-LR) + sin * 0.143f,
            M02 = LR + cos * (-LR) + sin * (-(1 - LR)),
            M03 = 0,
            M04 = 0,

            M10 = LG + cos * (-LG) + sin * (-LG),
            M11 = LG + cos * (1 - LG) + sin * 0.140f,
            M12 = LG + cos * (-LG) + sin * LG,
            M13 = 0,
            M14 = 0,

            M20 = LB + cos * (-LB) + sin * (1 - LB),
            M21 = LB + cos * (-LB) + sin * (-0.283f),
            M22 = LB + cos * (1 - LB) + sin * LB,
            M23 = 0,
            M24 = 0,

            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = 0,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Per-channel RGB tint/gain. Multiplies each channel independently.
    /// </summary>
    public static ColorMatrix ChannelGain(float red, float green, float blue)
    {
        return new ColorMatrix
        {
            M00 = red,
            M01 = 0,
            M02 = 0,
            M03 = 0,
            M04 = 0,
            M10 = 0,
            M11 = green,
            M12 = 0,
            M13 = 0,
            M14 = 0,
            M20 = 0,
            M21 = 0,
            M22 = blue,
            M23 = 0,
            M24 = 0,
            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = 0,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Protanopia (red-blind) correction filter.
    /// Shifts red information into perceivable channels.
    /// </summary>
    public static ColorMatrix ProtanopiaCorrection() => new()
    {
        M00 = 0.567f,
        M01 = 0.558f,
        M02 = 0,
        M03 = 0,
        M04 = 0,
        M10 = 0.433f,
        M11 = 0.442f,
        M12 = 0.242f,
        M13 = 0,
        M14 = 0,
        M20 = 0,
        M21 = 0,
        M22 = 0.758f,
        M23 = 0,
        M24 = 0,
        M30 = 0,
        M31 = 0,
        M32 = 0,
        M33 = 1,
        M34 = 0,
        M40 = 0,
        M41 = 0,
        M42 = 0,
        M43 = 0,
        M44 = 1,
    };

    /// <summary>
    /// Deuteranopia (green-blind) correction filter.
    /// </summary>
    public static ColorMatrix DeuteranopiaCorrection() => new()
    {
        M00 = 0.625f,
        M01 = 0.700f,
        M02 = 0,
        M03 = 0,
        M04 = 0,
        M10 = 0.375f,
        M11 = 0.300f,
        M12 = 0.300f,
        M13 = 0,
        M14 = 0,
        M20 = 0,
        M21 = 0,
        M22 = 0.700f,
        M23 = 0,
        M24 = 0,
        M30 = 0,
        M31 = 0,
        M32 = 0,
        M33 = 1,
        M34 = 0,
        M40 = 0,
        M41 = 0,
        M42 = 0,
        M43 = 0,
        M44 = 1,
    };

    /// <summary>
    /// Tritanopia (blue-blind) correction filter.
    /// </summary>
    public static ColorMatrix TritanopiaCorrection() => new()
    {
        M00 = 0.950f,
        M01 = 0,
        M02 = 0,
        M03 = 0,
        M04 = 0,
        M10 = 0.050f,
        M11 = 0.433f,
        M12 = 0.475f,
        M13 = 0,
        M14 = 0,
        M20 = 0,
        M21 = 0.567f,
        M22 = 0.525f,
        M23 = 0,
        M24 = 0,
        M30 = 0,
        M31 = 0,
        M32 = 0,
        M33 = 1,
        M34 = 0,
        M40 = 0,
        M41 = 0,
        M42 = 0,
        M43 = 0,
        M44 = 1,
    };

    /// <summary>
    /// Negative image with adjustable strength.
    /// 0 = identity, 1 = full inversion
    /// </summary>
    public static ColorMatrix Negative(float strength)
    {
        return Invert().WithStrength(strength);
    }

    /// <summary>
    /// Per-channel RGB offset. Adds a constant to each channel independently.
    /// This is the 5th column of the matrix — the additive offset.
    /// Unlocks: split toning, color casts, black point lift, blue light filter.
    /// Range: -1.0 to +1.0 per channel
    /// </summary>
    public static ColorMatrix ChannelOffset(float R, float G, float B)
    {
        return new ColorMatrix
        {
            M00 = 1,
            M01 = 0,
            M02 = 0,
            M03 = 0,
            M04 = 0,
            M10 = 0,
            M11 = 1,
            M12 = 0,
            M13 = 0,
            M14 = 0,
            M20 = 0,
            M21 = 0,
            M22 = 1,
            M23 = 0,
            M24 = 0,
            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = R,
            M41 = G,
            M42 = B,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Adjusts black and white points independently (like Levels in Photoshop).
    /// Compresses the input range [blackPoint, whitePoint] to full output range [0, 1].
    /// blackPoint: 0.0 to 0.3 (raises blacks, creates "lifted" look)
    /// whitePoint: 0.7 to 1.0 (lowers ceiling, creates "faded" look)
    /// </summary>
    public static ColorMatrix Levels(float blackPoint, float whitePoint)
    {
        float scale = 1.0f / (whitePoint - blackPoint);
        float offset = -blackPoint * scale;

        return new ColorMatrix
        {
            M00 = scale,
            M01 = 0,
            M02 = 0,
            M03 = 0,
            M04 = 0,
            M10 = 0,
            M11 = scale,
            M12 = 0,
            M13 = 0,
            M14 = 0,
            M20 = 0,
            M21 = 0,
            M22 = scale,
            M23 = 0,
            M24 = 0,
            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = offset,
            M41 = offset,
            M42 = offset,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Vibrance — selective saturation that affects muted colors more than saturated ones.
    /// More natural looking than regular saturation. Protects skin tones.
    /// amount: -1.0 (desaturate muted colors) to 1.0 (boost muted colors)
    /// Matrix approximation of true vibrance algorithm.
    /// </summary>
    public static ColorMatrix Vibrance(float amount)
    {
        // Vibrance is weighted saturation that favors muted colors
        // This is a matrix approximation - true vibrance needs per-pixel HSL analysis
        float s = 1.0f + (amount * 0.5f);
        float invSat = 1.0f - s;
        float rwgt = LR * invSat;
        float gwgt = LG * invSat;
        float bwgt = LB * invSat;

        return new ColorMatrix
        {
            M00 = rwgt + s,
            M01 = rwgt,
            M02 = rwgt,
            M03 = 0,
            M04 = 0,
            M10 = gwgt,
            M11 = gwgt + s,
            M12 = gwgt,
            M13 = 0,
            M14 = 0,
            M20 = bwgt,
            M21 = bwgt,
            M22 = bwgt + s,
            M23 = 0,
            M24 = 0,
            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = 0,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Photographic exposure adjustment in stops.
    /// Each stop doubles or halves the amount of light.
    /// stops: -3.0 (much darker) to +3.0 (much brighter)
    /// </summary>
    public static ColorMatrix Exposure(float stops)
    {
        float multiplier = MathF.Pow(2.0f, stops);
        return new ColorMatrix
        {
            M00 = multiplier,
            M01 = 0,
            M02 = 0,
            M03 = 0,
            M04 = 0,
            M10 = 0,
            M11 = multiplier,
            M12 = 0,
            M13 = 0,
            M14 = 0,
            M20 = 0,
            M21 = 0,
            M22 = multiplier,
            M23 = 0,
            M24 = 0,
            M30 = 0,
            M31 = 0,
            M32 = 0,
            M33 = 1,
            M34 = 0,
            M40 = 0,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1,
        };
    }

    /// <summary>
    /// Color temperature shift in Kelvin.
    /// Based on blackbody radiation color approximation.
    /// kelvin: 2000K (warm/candlelight) to 10000K (cool/overcast). 6500K = daylight neutral.
    /// </summary>
    public static ColorMatrix ColorTemperature(float kelvin)
    {
        // Clamp to reasonable range
        kelvin = Math.Clamp(kelvin, 1000f, 40000f) / 100f;

        float r, g, b;

        // Red calculation
        if (kelvin <= 66)
            r = 1.0f;
        else
            r = Math.Clamp(1.292936186f * MathF.Pow(kelvin - 60, -0.1332047592f), 0f, 1f);

        // Green calculation
        if (kelvin <= 66)
            g = Math.Clamp(0.39008157876901f * MathF.Log(kelvin) - 0.631841443f, 0f, 1f);
        else
            g = Math.Clamp(1.129897805f * MathF.Pow(kelvin - 60, -0.0755148492f), 0f, 1f);

        // Blue calculation
        if (kelvin >= 66)
            b = 1.0f;
        else if (kelvin <= 19)
            b = 0.0f;
        else
            b = Math.Clamp(0.543206789f * MathF.Log(kelvin - 10) - 1.196254089f, 0f, 1f);

        return ChannelGain(r, g, b);
    }

    /// <summary>
    /// Approximates split toning — different color tints for shadows vs highlights.
    /// This is a MATRIX APPROXIMATION of true luminance-based split toning.
    /// shadowTint: Color to add to darker areas (e.g., orange, blue)
    /// highlightTint: Color to add to brighter areas (e.g., teal, yellow)
    /// balance: 0.0 (all shadows) to 1.0 (all highlights)
    /// </summary>
    public static ColorMatrix SplitTone(Color shadowTint, Color highlightTint, float balance = 0.5f)
    {
        // Extract normalized RGB (0-1 range)
        float sR = shadowTint.R / 255f;
        float sG = shadowTint.G / 255f;
        float sB = shadowTint.B / 255f;

        float hR = highlightTint.R / 255f;
        float hG = highlightTint.G / 255f;
        float hB = highlightTint.B / 255f;

        // Matrix approximation: offset affects shadows, gain affects highlights
        // This is not true luminance-based split toning but works reasonably well
        return ColorMatrix.Multiply(
            ChannelOffset(sR * balance * 0.1f, sG * balance * 0.1f, sB * balance * 0.1f),
            ChannelGain(
                1.0f + (hR - 0.5f) * (1 - balance) * 0.2f,
                1.0f + (hG - 0.5f) * (1 - balance) * 0.2f,
                1.0f + (hB - 0.5f) * (1 - balance) * 0.2f
            )
        );
    }

    #region Film Look Presets

    /// <summary>
    /// Warm Film — lifted blacks with orange/teal split toning.
    /// Classic cinema look with raised black point and color separation.
    /// </summary>
    public static ColorMatrix WarmFilm()
    {
        var lifted = Levels(0.08f, 1.0f);                      // Raise blacks
        var warm = ChannelGain(1.1f, 1.0f, 0.9f);              // Orange tint
        var tealShadows = ChannelOffset(0.0f, 0.02f, 0.03f);   // Teal in shadows
        return ColorMatrix.Multiply(ColorMatrix.Multiply(lifted, warm), tealShadows);
    }

    /// <summary>
    /// Cool Film — crushed shadows with desaturated blues.
    /// Dramatic high-contrast look with cooler color palette.
    /// </summary>
    public static ColorMatrix CoolFilm()
    {
        var crushed = Contrast(1.3f);                          // Crush shadows
        var desat = Saturation(0.7f);                          // Desaturate
        var cool = ChannelGain(0.95f, 1.0f, 1.05f);            // Cool tint
        return ColorMatrix.Multiply(ColorMatrix.Multiply(crushed, desat), cool);
    }

    /// <summary>
    /// Cyberpunk — boosted magentas and cyans with crushed midtones.
    /// Neon-inspired color palette with high contrast.
    /// </summary>
    public static ColorMatrix Cyberpunk()
    {
        var magenta = ChannelGain(1.3f, 0.9f, 1.4f);           // Boost R+B (magenta)
        var cyan = ChannelOffset(-0.05f, 0.05f, 0.1f);         // Cyan cast
        var crush = Contrast(1.4f);                            // Crushed mids
        return ColorMatrix.Multiply(ColorMatrix.Multiply(magenta, cyan), crush);
    }

    /// <summary>
    /// Noir — high contrast black and white with preserved highlights.
    /// Classic film noir aesthetic with deep blacks and bright highlights.
    /// </summary>
    public static ColorMatrix Noir()
    {
        var bw = Grayscale();                                  // Convert to B&W
        var highContrast = Contrast(1.6f);                     // High contrast
        var liftBlacks = Levels(0.05f, 0.95f);                 // Slight compression
        return ColorMatrix.Multiply(ColorMatrix.Multiply(bw, highContrast), liftBlacks);
    }

    /// <summary>
    /// Vintage — faded colors with lifted blacks and warm cast.
    /// Aged photograph look with reduced saturation and compressed tonal range.
    /// </summary>
    public static ColorMatrix Vintage()
    {
        var faded = Saturation(0.6f);                          // Fade colors
        var lifted = Levels(0.12f, 0.93f);                     // Lifted blacks, lowered whites
        var warm = ChannelGain(1.1f, 1.0f, 0.85f);             // Warm cast
        var sepia = ChannelOffset(0.05f, 0.03f, 0.0f);         // Slight sepia tint
        return ColorMatrix.Multiply(
            ColorMatrix.Multiply(
                ColorMatrix.Multiply(faded, lifted),
                warm),
            sepia);
    }

    /// <summary>
    /// HDR Effect — simulated high dynamic range via contrast and saturation boost.
    /// Matrix approximation of HDR tonemapping (not true local contrast).
    /// </summary>
    public static ColorMatrix HDREffect()
    {
        var contrast = Contrast(1.3f);                         // Boost contrast
        var saturation = Saturation(1.4f);                     // Boost saturation
        var exposure = Exposure(0.2f);                         // Slight exposure lift
        return ColorMatrix.Multiply(ColorMatrix.Multiply(contrast, saturation), exposure);
    }

    #endregion
}

#endregion

#region Filter Profile

public enum FilterPreset
{
    None,
    Grayscale,
    Inverted,
    Sepia,
    ProtanopiaCorrection,
    DeuteranopiaCorrection,
    TritanopiaCorrection,
    HighContrast,
    NightLight,

    // Film Look Presets
    WarmFilm,
    CoolFilm,
    Cyberpunk,
    Noir,
    Vintage,
    HDREffect
}

/// <summary>
/// Composable filter profile. Each property generates a matrix,
/// and they're all multiplied together to produce the final effect.
/// </summary>
public class FilterProfile
{
    // Preset (applied first, before adjustments)
    public FilterPreset Preset { get; set; } = FilterPreset.None;
    public float PresetStrength { get; set; } = 1.0f;       // 0.0 to 1.0

    // Adjustable parameters (composed on top of preset)
    public float Brightness { get; set; } = 0.0f;           // -1.0 to 1.0
    public float Contrast { get; set; } = 1.0f;             // 0.0 to 2.0
    public float Saturation { get; set; } = 1.0f;           // 0.0 to 3.0
    public float HueRotation { get; set; } = 0.0f;          // -180 to 180 degrees
    public float InversionStrength { get; set; } = 0.0f;    // 0.0 to 1.0

    // Channel gains
    public float RedGain { get; set; } = 1.0f;              // 0.0 to 2.0
    public float GreenGain { get; set; } = 1.0f;            // 0.0 to 2.0
    public float BlueGain { get; set; } = 1.0f;             // 0.0 to 2.0

    // Channel offsets (additive)
    public float RedOffset { get; set; } = 0.0f;            // -1.0 to 1.0
    public float GreenOffset { get; set; } = 0.0f;          // -1.0 to 1.0
    public float BlueOffset { get; set; } = 0.0f;           // -1.0 to 1.0

    // Advanced adjustments
    public float BlackPoint { get; set; } = 0.0f;           // 0.0 to 0.3 (lifts blacks)
    public float WhitePoint { get; set; } = 1.0f;           // 0.7 to 1.0 (lowers ceiling)
    public float Vibrance { get; set; } = 0.0f;             // -1.0 to 1.0 (selective saturation)
    public float Exposure { get; set; } = 0.0f;             // -3.0 to 3.0 (stops)
    public float Temperature { get; set; } = 6500f;         // 2000K to 10000K

    // Split toning (shadow/highlight tints)
    public Color ShadowTint { get; set; } = Colors.Transparent;
    public Color HighlightTint { get; set; } = Colors.Transparent;
    public float ToneBalance { get; set; } = 0.5f;          // 0.0 to 1.0


    /// <summary>
    /// Creates a deep copy of this filter profile.
    /// </summary>
    public FilterProfile Clone() => new()
    {
        Preset = this.Preset,
        PresetStrength = this.PresetStrength,
        Brightness = this.Brightness,
        Contrast = this.Contrast,
        Saturation = this.Saturation,
        HueRotation = this.HueRotation,
        InversionStrength = this.InversionStrength,
        RedGain = this.RedGain,
        GreenGain = this.GreenGain,
        BlueGain = this.BlueGain,
        RedOffset = this.RedOffset,
        GreenOffset = this.GreenOffset,
        BlueOffset = this.BlueOffset,
        BlackPoint = this.BlackPoint,
        WhitePoint = this.WhitePoint,
        Vibrance = this.Vibrance,
        Exposure = this.Exposure,
        Temperature = this.Temperature,
        ShadowTint = this.ShadowTint,
        HighlightTint = this.HighlightTint,
        ToneBalance = this.ToneBalance
    };
    /// <summary>
    /// Builds the final composite 5x5 color matrix from all parameters.
    /// Composition order: Preset -> Exposure -> Temperature -> Levels -> 
    ///                    Inversion -> Vibrance -> Saturation -> Hue -> 
    ///                    Channel Gains -> Channel Offsets -> Split Tone -> 
    ///                    Contrast -> Brightness
    /// </summary>
    public ColorMatrix BuildMatrix()
    {
        var result = ColorMatrix.Identity;

        // 1. Preset base matrix
        if (Preset != FilterPreset.None)
        {
            var presetMatrix = GetPresetMatrix(Preset);
            if (PresetStrength < 0.999f)
                presetMatrix = presetMatrix.WithStrength(PresetStrength);
            result = ColorMatrix.Multiply(presetMatrix, result);
        }

        // 2. Exposure (early, affects all subsequent operations)
        if (MathF.Abs(Exposure) > 0.001f)
        {
            var exp = FilterMatrices.Exposure(Exposure);
            result = ColorMatrix.Multiply(exp, result);
        }

        // 3. Color Temperature (early, sets color foundation)
        if (MathF.Abs(Temperature - 6500f) > 10f)
        {
            var temp = FilterMatrices.ColorTemperature(Temperature);
            result = ColorMatrix.Multiply(temp, result);
        }

        // 4. Levels (black/white point adjustment)
        if (MathF.Abs(BlackPoint) > 0.001f || MathF.Abs(WhitePoint - 1.0f) > 0.001f)
        {
            var levels = FilterMatrices.Levels(BlackPoint, WhitePoint);
            result = ColorMatrix.Multiply(levels, result);
        }

        // 5. Inversion
        if (InversionStrength > 0.001f)
        {
            var inv = FilterMatrices.Negative(InversionStrength);
            result = ColorMatrix.Multiply(inv, result);
        }

        // 6. Vibrance (before saturation for better results)
        if (MathF.Abs(Vibrance) > 0.001f)
        {
            var vib = FilterMatrices.Vibrance(Vibrance);
            result = ColorMatrix.Multiply(vib, result);
        }

        // 7. Saturation
        if (MathF.Abs(Saturation - 1.0f) > 0.001f)
        {
            var sat = FilterMatrices.Saturation(Saturation);
            result = ColorMatrix.Multiply(sat, result);
        }

        // 8. Hue rotation
        if (MathF.Abs(HueRotation) > 0.1f)
        {
            var hue = FilterMatrices.HueRotation(HueRotation);
            result = ColorMatrix.Multiply(hue, result);
        }

        // 9. Channel gains
        if (MathF.Abs(RedGain - 1.0f) > 0.001f ||
            MathF.Abs(GreenGain - 1.0f) > 0.001f ||
            MathF.Abs(BlueGain - 1.0f) > 0.001f)
        {
            var gains = FilterMatrices.ChannelGain(RedGain, GreenGain, BlueGain);
            result = ColorMatrix.Multiply(gains, result);
        }

        // 10. Channel offsets
        if (MathF.Abs(RedOffset) > 0.001f ||
            MathF.Abs(GreenOffset) > 0.001f ||
            MathF.Abs(BlueOffset) > 0.001f)
        {
            var offsets = FilterMatrices.ChannelOffset(RedOffset, GreenOffset, BlueOffset);
            result = ColorMatrix.Multiply(offsets, result);
        }

        // 11. Split toning (if tints are not transparent)
        if (ShadowTint != Colors.Transparent || HighlightTint != Colors.Transparent)
        {
            var split = FilterMatrices.SplitTone(
                ShadowTint == Colors.Transparent ? Colors.Gray : ShadowTint,
                HighlightTint == Colors.Transparent ? Colors.Gray : HighlightTint,
                ToneBalance
            );
            result = ColorMatrix.Multiply(split, result);
        }

        // 12. Contrast
        if (MathF.Abs(Contrast - 1.0f) > 0.001f)
        {
            var con = FilterMatrices.Contrast(Contrast);
            result = ColorMatrix.Multiply(con, result);
        }

        // 13. Brightness (last, final brightness adjustment)
        if (MathF.Abs(Brightness) > 0.001f)
        {
            var brt = FilterMatrices.Brightness(Brightness);
            result = ColorMatrix.Multiply(brt, result);
        }

        return result;
    }

    private static ColorMatrix GetPresetMatrix(FilterPreset preset) => preset switch
    {
        FilterPreset.Grayscale => FilterMatrices.Grayscale(),
        FilterPreset.Inverted => FilterMatrices.Invert(),
        FilterPreset.Sepia => FilterMatrices.Sepia(),
        FilterPreset.ProtanopiaCorrection => FilterMatrices.ProtanopiaCorrection(),
        FilterPreset.DeuteranopiaCorrection => FilterMatrices.DeuteranopiaCorrection(),
        FilterPreset.TritanopiaCorrection => FilterMatrices.TritanopiaCorrection(),
        FilterPreset.HighContrast => FilterMatrices.Contrast(1.6f),
        FilterPreset.NightLight => ColorMatrix.Multiply(
            FilterMatrices.Saturation(0.8f),
            ColorMatrix.Multiply(
                FilterMatrices.ChannelGain(1.0f, 0.9f, 0.6f),
                FilterMatrices.Brightness(-0.05f)
            )
        ),
        FilterPreset.WarmFilm => FilterMatrices.WarmFilm(),
        FilterPreset.CoolFilm => FilterMatrices.CoolFilm(),
        FilterPreset.Cyberpunk => FilterMatrices.Cyberpunk(),
        FilterPreset.Noir => FilterMatrices.Noir(),
        FilterPreset.Vintage => FilterMatrices.Vintage(),
        FilterPreset.HDREffect => FilterMatrices.HDREffect(),
        _ => ColorMatrix.Identity,
    };

    /// <summary>
    /// Creates a FilterProfile from a preset with default adjustments.
    /// </summary>
    public static FilterProfile FromPreset(FilterPreset preset) => new()
    {
        Preset = preset,
        PresetStrength = 1.0f
    };

    /// <summary>
    /// Returns true if this profile produces the identity matrix (no effect).
    /// </summary>
    public bool IsIdentity()
    {
        return Preset == FilterPreset.None &&
               MathF.Abs(Brightness) < 0.001f &&
               MathF.Abs(Contrast - 1.0f) < 0.001f &&
               MathF.Abs(Saturation - 1.0f) < 0.001f &&
               MathF.Abs(HueRotation) < 0.1f &&
               MathF.Abs(InversionStrength) < 0.001f &&
               MathF.Abs(RedGain - 1.0f) < 0.001f &&
               MathF.Abs(GreenGain - 1.0f) < 0.001f &&
               MathF.Abs(BlueGain - 1.0f) < 0.001f &&
               MathF.Abs(RedOffset) < 0.001f &&
               MathF.Abs(GreenOffset) < 0.001f &&
               MathF.Abs(BlueOffset) < 0.001f &&
               MathF.Abs(BlackPoint) < 0.001f &&
               MathF.Abs(WhitePoint - 1.0f) < 0.001f &&
               MathF.Abs(Vibrance) < 0.001f &&
               MathF.Abs(Exposure) < 0.001f &&
               MathF.Abs(Temperature - 6500f) < 10f &&
               ShadowTint == Colors.Transparent &&
               HighlightTint == Colors.Transparent;
    }
}

#endregion