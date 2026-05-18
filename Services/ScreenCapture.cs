using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DisplayControl.Services;

/// <summary>
/// Captures screen regions and extracts pixel data for histogram analysis.
/// </summary>
public static class ScreenCapture
{
    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth,
        int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        [Out] byte[] lpvBits, ref BITMAPINFO lpbmi, uint uUsage);

    private const int SRCCOPY = 0x00CC0020;
    private const int DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public uint[] bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    #endregion

    /// <summary>
    /// Captures the entire primary screen and returns raw BGRA pixel data.
    /// </summary>
    public static (byte[] pixels, int width, int height, int stride) CapturePrimaryScreen()
    {
        int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
        int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
        return CaptureRegion(0, 0, screenWidth, screenHeight);
    }

    /// <summary>
    /// Captures a specific screen region and returns raw BGRA pixel data.
    /// Coordinates are in screen space (not DPI-scaled).
    /// </summary>
    public static (byte[] pixels, int width, int height, int stride) CaptureRegion(
        int x, int y, int width, int height)
    {
        IntPtr screenDC = IntPtr.Zero;
        IntPtr memDC = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            // Get screen DC
            screenDC = GetDC(IntPtr.Zero);
            memDC = CreateCompatibleDC(screenDC);

            // Create bitmap
            hBitmap = CreateCompatibleBitmap(screenDC, width, height);
            SelectObject(memDC, hBitmap);

            // Copy screen to bitmap
            BitBlt(memDC, 0, 0, width, height, screenDC, x, y, SRCCOPY);

            // Extract pixel data
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // Top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0 // BI_RGB
                }
            };

            int stride = ((width * 32 + 31) / 32) * 4;
            byte[] pixels = new byte[stride * height];

            GetDIBits(memDC, hBitmap, 0, (uint)height, pixels, ref bmi, DIB_RGB_COLORS);

            return (pixels, width, height, stride);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDC != IntPtr.Zero) DeleteDC(memDC);
            if (screenDC != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    /// <summary>
    /// Captures screen and downsamples to target size for performance.
    /// Good for histogram analysis where exact resolution doesn't matter.
    /// </summary>
    public static (byte[] pixels, int width, int height, int stride) CapturePrimaryScreenDownsampled(
        int targetWidth = 640, int targetHeight = 360)
    {
        var (fullPixels, fullWidth, fullHeight, fullStride) = CapturePrimaryScreen();

        // Simple nearest-neighbor downsample for speed
        int stride = ((targetWidth * 32 + 31) / 32) * 4;
        byte[] downsampled = new byte[stride * targetHeight];

        double scaleX = (double)fullWidth / targetWidth;
        double scaleY = (double)fullHeight / targetHeight;

        for (int y = 0; y < targetHeight; y++)
        {
            int srcY = (int)(y * scaleY);
            int srcRowOffset = srcY * fullStride;
            int dstRowOffset = y * stride;

            for (int x = 0; x < targetWidth; x++)
            {
                int srcX = (int)(x * scaleX);
                int srcPixel = srcRowOffset + srcX * 4;
                int dstPixel = dstRowOffset + x * 4;

                downsampled[dstPixel] = fullPixels[srcPixel];         // B
                downsampled[dstPixel + 1] = fullPixels[srcPixel + 1]; // G
                downsampled[dstPixel + 2] = fullPixels[srcPixel + 2]; // R
                downsampled[dstPixel + 3] = fullPixels[srcPixel + 3]; // A
            }
        }

        return (downsampled, targetWidth, targetHeight, stride);
    }
}