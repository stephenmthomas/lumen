using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DisplayControl.Controls;

/// <summary>
/// Displays a histogram for color channels (R, G, B, RGB composite, Luminance).
/// Renders filled, semi-transparent overlapping channel curves with
/// the same grid/background aesthetic as CurveEditorControl.
/// Feed it raw 256-bin arrays via SetHistogram or bind an image source.
/// </summary>
public class HistogramControl : Control
{
    #region Dependency Properties

    public static readonly DependencyProperty GridColorProperty =
        DependencyProperty.Register(nameof(GridColor), typeof(Brush), typeof(HistogramControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridMinorColorProperty =
        DependencyProperty.Register(nameof(GridMinorColor), typeof(Brush), typeof(HistogramControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowRedProperty =
        DependencyProperty.Register(nameof(ShowRed), typeof(bool), typeof(HistogramControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGreenProperty =
        DependencyProperty.Register(nameof(ShowGreen), typeof(bool), typeof(HistogramControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowBlueProperty =
        DependencyProperty.Register(nameof(ShowBlue), typeof(bool), typeof(HistogramControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowRGBProperty =
        DependencyProperty.Register(nameof(ShowRGB), typeof(bool), typeof(HistogramControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowLuminanceProperty =
        DependencyProperty.Register(nameof(ShowLuminance), typeof(bool), typeof(HistogramControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RedColorProperty =
        DependencyProperty.Register(nameof(RedColor), typeof(Color), typeof(HistogramControl),
            new FrameworkPropertyMetadata(Color.FromRgb(220, 60, 60),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GreenColorProperty =
        DependencyProperty.Register(nameof(GreenColor), typeof(Color), typeof(HistogramControl),
            new FrameworkPropertyMetadata(Color.FromRgb(60, 200, 80),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BlueColorProperty =
        DependencyProperty.Register(nameof(BlueColor), typeof(Color), typeof(HistogramControl),
            new FrameworkPropertyMetadata(Color.FromRgb(60, 120, 220),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RGBColorProperty =
        DependencyProperty.Register(nameof(RGBColor), typeof(Color), typeof(HistogramControl),
            new FrameworkPropertyMetadata(Color.FromRgb(180, 180, 180),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LuminanceColorProperty =
        DependencyProperty.Register(nameof(LuminanceColor), typeof(Color), typeof(HistogramControl),
            new FrameworkPropertyMetadata(Color.FromRgb(200, 180, 100),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillOpacityProperty =
        DependencyProperty.Register(nameof(FillOpacity), typeof(double), typeof(HistogramControl),
            new FrameworkPropertyMetadata(0.35, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(HistogramControl),
            new FrameworkPropertyMetadata(1.2, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UseLogScaleProperty =
        DependencyProperty.Register(nameof(UseLogScale), typeof(bool), typeof(HistogramControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush GridColor
    {
        get => (Brush)GetValue(GridColorProperty);
        set => SetValue(GridColorProperty, value);
    }

    public Brush GridMinorColor
    {
        get => (Brush)GetValue(GridMinorColorProperty);
        set => SetValue(GridMinorColorProperty, value);
    }

    public bool ShowRed
    {
        get => (bool)GetValue(ShowRedProperty);
        set => SetValue(ShowRedProperty, value);
    }

    public bool ShowGreen
    {
        get => (bool)GetValue(ShowGreenProperty);
        set => SetValue(ShowGreenProperty, value);
    }

    public bool ShowBlue
    {
        get => (bool)GetValue(ShowBlueProperty);
        set => SetValue(ShowBlueProperty, value);
    }

    /// <summary>
    /// When true, draws a combined RGB histogram (sum of R+G+B per bin)
    /// as a single composite channel.
    /// </summary>
    public bool ShowRGB
    {
        get => (bool)GetValue(ShowRGBProperty);
        set => SetValue(ShowRGBProperty, value);
    }

    public bool ShowLuminance
    {
        get => (bool)GetValue(ShowLuminanceProperty);
        set => SetValue(ShowLuminanceProperty, value);
    }

    public Color RedColor
    {
        get => (Color)GetValue(RedColorProperty);
        set => SetValue(RedColorProperty, value);
    }

    public Color GreenColor
    {
        get => (Color)GetValue(GreenColorProperty);
        set => SetValue(GreenColorProperty, value);
    }

    public Color BlueColor
    {
        get => (Color)GetValue(BlueColorProperty);
        set => SetValue(BlueColorProperty, value);
    }

    /// <summary>
    /// Color used for the combined RGB composite histogram.
    /// </summary>
    public Color RGBColor
    {
        get => (Color)GetValue(RGBColorProperty);
        set => SetValue(RGBColorProperty, value);
    }

    public Color LuminanceColor
    {
        get => (Color)GetValue(LuminanceColorProperty);
        set => SetValue(LuminanceColorProperty, value);
    }

    /// <summary>
    /// Opacity of the filled region beneath each channel curve (0.0–1.0).
    /// </summary>
    public double FillOpacity
    {
        get => (double)GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// When true, applies log scaling to bin values for better shadow/highlight visibility.
    /// </summary>
    public bool UseLogScale
    {
        get => (bool)GetValue(UseLogScaleProperty);
        set => SetValue(UseLogScaleProperty, value);
    }

    #endregion

    #region Histogram Data

    private int[] _redBins = new int[256];
    private int[] _greenBins = new int[256];
    private int[] _blueBins = new int[256];
    private int[] _rgbBins = new int[256];
    private int[] _luminanceBins = new int[256];

    /// <summary>
    /// Rebuilds the composite RGB bin array from the current per-channel data.
    /// Each RGB bin = red[i] + green[i] + blue[i].
    /// </summary>
    private void RebuildRGBBins()
    {
        for (int i = 0; i < 256; i++)
            _rgbBins[i] = _redBins[i] + _greenBins[i] + _blueBins[i];
    }

    /// <summary>
    /// Sets histogram data for one or more channels. Each array must be exactly 256 elements.
    /// Pass null for any channel to leave it unchanged.
    /// The RGB composite channel is automatically recomputed.
    /// </summary>
    public void SetHistogram(int[]? red = null, int[]? green = null, int[]? blue = null, int[]? luminance = null)
    {
        if (red is { Length: 256 }) Array.Copy(red, _redBins, 256);
        if (green is { Length: 256 }) Array.Copy(green, _greenBins, 256);
        if (blue is { Length: 256 }) Array.Copy(blue, _blueBins, 256);
        if (luminance is { Length: 256 }) Array.Copy(luminance, _luminanceBins, 256);
        RebuildRGBBins();
        InvalidateVisual();
    }

    /// <summary>
    /// Computes histogram bins from a raw pixel buffer (32-bit BGRA, row-major).
    /// Luminance is calculated as 0.2126R + 0.7152G + 0.0722B (BT.709).
    /// The RGB composite channel is built automatically.
    /// </summary>
    public void SetFromPixelData(byte[] pixels, int stride, int width, int height)
    {
        Array.Clear(_redBins);
        Array.Clear(_greenBins);
        Array.Clear(_blueBins);
        Array.Clear(_luminanceBins);

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = rowOffset + x * 4;
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                // alpha at pixels[i + 3] ignored

                _blueBins[b]++;
                _greenBins[g]++;
                _redBins[r]++;

                int lum = (int)(0.2126 * r + 0.7152 * g + 0.0722 * b);
                _luminanceBins[Math.Clamp(lum, 0, 255)]++;
            }
        }

        RebuildRGBBins();
        InvalidateVisual();
    }

    /// <summary>
    /// Clears all histogram data.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_redBins);
        Array.Clear(_greenBins);
        Array.Clear(_blueBins);
        Array.Clear(_rgbBins);
        Array.Clear(_luminanceBins);
        InvalidateVisual();
    }

    #endregion

    private const double Padding = 0.0;

    public HistogramControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        MinHeight = 150;
        MinWidth = 150;
        ClipToBounds = true;
    }

    #region Rendering

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 1 || h < 1) return;

        // Background
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));

        // Grid — matches CurveEditorControl exactly
        var gridPen = new Pen(GridColor, 0.5);
        var minorPen = new Pen(GridMinorColor, 0.5);
        gridPen.Freeze();
        minorPen.Freeze();

        double drawW = w - 2 * Padding;
        double drawH = h - 2 * Padding;

        // Major grid (4×4)
        for (int i = 1; i < 4; i++)
        {
            double frac = i / 4.0;
            double gx = Padding + frac * drawW;
            double gy = Padding + frac * drawH;
            dc.DrawLine(gridPen, new Point(gx, Padding), new Point(gx, h - Padding));
            dc.DrawLine(gridPen, new Point(Padding, gy), new Point(w - Padding, gy));
        }

        // Minor grid
        for (int i = 1; i < 5; i++)
        {
            double minorFrac = (i - 0.5) / 4.0;
            double mx = Padding + minorFrac * drawW;
            double my = Padding + minorFrac * drawH;
            dc.DrawLine(minorPen, new Point(mx, Padding), new Point(mx, h - Padding));
            dc.DrawLine(minorPen, new Point(Padding, my), new Point(w - Padding, my));
        }

        // Border
        var borderPen = new Pen(GridColor, 1.0);
        borderPen.Freeze();
        dc.DrawRectangle(null, borderPen, new Rect(Padding, Padding, drawW, drawH));

        // Find global max across all visible channels for consistent scaling.
        // Each channel is independently scaled to the full draw height so that
        // every visible channel uses 100% of the control height.  When channels
        // share a single globalMax the smaller ones can look nearly flat — using
        // per-channel max avoids this.  The composite RGB bins are naturally
        // ~3× larger, so per-channel scaling also keeps them from dwarfing the
        // individual R/G/B curves.
        //
        // (If you prefer a shared scale so relative magnitudes are visible,
        //  replace the per-channel MaxBin calls in DrawChannel with globalMax.)
        int globalMax = 1;
        if (ShowRed) globalMax = Math.Max(globalMax, MaxBin(_redBins));
        if (ShowGreen) globalMax = Math.Max(globalMax, MaxBin(_greenBins));
        if (ShowBlue) globalMax = Math.Max(globalMax, MaxBin(_blueBins));
        if (ShowRGB) globalMax = Math.Max(globalMax, MaxBin(_rgbBins));
        if (ShowLuminance) globalMax = Math.Max(globalMax, MaxBin(_luminanceBins));

        double logMax = UseLogScale ? Math.Log(globalMax + 1) : 0;

        // Draw channels back-to-front: RGB composite, luminance, blue, green, red
        if (ShowRGB) DrawChannel(dc, _rgbBins, RGBColor, globalMax, logMax);
        if (ShowLuminance) DrawChannel(dc, _luminanceBins, LuminanceColor, globalMax, logMax);
        if (ShowBlue) DrawChannel(dc, _blueBins, BlueColor, globalMax, logMax);
        if (ShowGreen) DrawChannel(dc, _greenBins, GreenColor, globalMax, logMax);
        if (ShowRed) DrawChannel(dc, _redBins, RedColor, globalMax, logMax);
    }

    private void DrawChannel(DrawingContext dc, int[] bins, Color color, int globalMax, double logMax)
    {
        double w = ActualWidth - 2 * Padding;
        double h = ActualHeight - 2 * Padding;
        if (w < 1 || h < 1) return;

        double bottom = Padding + h;

        // Build the filled geometry
        var fillGeometry = new StreamGeometry();
        using (var ctx = fillGeometry.Open())
        {
            // Start at bottom-left
            ctx.BeginFigure(new Point(Padding, bottom), true, true);

            for (int i = 0; i < 256; i++)
            {
                double x = Padding + (i / 255.0) * w;
                double normalized = UseLogScale
                    ? (logMax > 0 ? Math.Log(bins[i] + 1) / logMax : 0)
                    : (double)bins[i] / globalMax;
                // Clamp so nothing renders outside the draw area
                normalized = Math.Clamp(normalized, 0.0, 1.0);
                double y = bottom - normalized * h;
                ctx.LineTo(new Point(x, y), true, true);
            }

            // Close along the bottom edge
            ctx.LineTo(new Point(Padding + w, bottom), true, false);
        }
        fillGeometry.Freeze();

        // Fill with semi-transparent version
        var fillColor = Color.FromArgb((byte)(Math.Clamp(FillOpacity, 0.0, 1.0) * 255), color.R, color.G, color.B);
        var fillBrush = new SolidColorBrush(fillColor);
        fillBrush.Freeze();
        dc.DrawGeometry(fillBrush, null, fillGeometry);

        // Stroke the top edge only (not the baseline)
        var strokeGeometry = new StreamGeometry();
        using (var ctx = strokeGeometry.Open())
        {
            double x0 = Padding;
            double normalized0 = UseLogScale
                ? (logMax > 0 ? Math.Log(bins[0] + 1) / logMax : 0)
                : (double)bins[0] / globalMax;
            normalized0 = Math.Clamp(normalized0, 0.0, 1.0);
            double y0 = bottom - normalized0 * h;
            ctx.BeginFigure(new Point(x0, y0), false, false);

            for (int i = 1; i < 256; i++)
            {
                double x = Padding + (i / 255.0) * w;
                double normalized = UseLogScale
                    ? (logMax > 0 ? Math.Log(bins[i] + 1) / logMax : 0)
                    : (double)bins[i] / globalMax;
                normalized = Math.Clamp(normalized, 0.0, 1.0);
                double y = bottom - normalized * h;
                ctx.LineTo(new Point(x, y), true, false);
            }
        }
        strokeGeometry.Freeze();

        var strokeBrush = new SolidColorBrush(color);
        strokeBrush.Freeze();
        var strokePen = new Pen(strokeBrush, StrokeThickness);
        strokePen.Freeze();
        dc.DrawGeometry(null, strokePen, strokeGeometry);
    }

    private static int MaxBin(int[] bins)
    {
        int max = 0;
        // Skip bins 0 and 255 to avoid pure-black/white spikes dominating the scale
        for (int i = 1; i < 255; i++)
        {
            if (bins[i] > max) max = bins[i];
        }
        return max;
    }

    #endregion
}