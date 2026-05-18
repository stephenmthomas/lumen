using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DisplayControl.Controls;

/// <summary>
/// Read-only display control that visualizes the current gamma ramp output.
/// Shows three overlaid RGB curves representing the final values being sent
/// to SetDeviceGammaRamp. Input (0-255) on X axis, Output (0-65535) on Y axis.
/// 
/// Usage:
///   <controls:GammaRampDisplay x:Name="GammaRampView" Height="200"/>
///   
/// Update from code-behind after applying a profile:
///   GammaRampView.UpdateRamp(ramp);
/// </summary>
public class GammaRampDisplay : Control
{
    #region Dependency Properties

    public static readonly DependencyProperty ShowRedProperty =
        DependencyProperty.Register(nameof(ShowRed), typeof(bool), typeof(GammaRampDisplay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGreenProperty =
        DependencyProperty.Register(nameof(ShowGreen), typeof(bool), typeof(GammaRampDisplay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowBlueProperty =
        DependencyProperty.Register(nameof(ShowBlue), typeof(bool), typeof(GammaRampDisplay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowIdentityProperty =
        DependencyProperty.Register(nameof(ShowIdentity), typeof(bool), typeof(GammaRampDisplay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowRed { get => (bool)GetValue(ShowRedProperty); set => SetValue(ShowRedProperty, value); }
    public bool ShowGreen { get => (bool)GetValue(ShowGreenProperty); set => SetValue(ShowGreenProperty, value); }
    public bool ShowBlue { get => (bool)GetValue(ShowBlueProperty); set => SetValue(ShowBlueProperty, value); }
    public bool ShowIdentity { get => (bool)GetValue(ShowIdentityProperty); set => SetValue(ShowIdentityProperty, value); }

    #endregion

    #region Ramp Data

    private ushort[] _red = new ushort[256];
    private ushort[] _green = new ushort[256];
    private ushort[] _blue = new ushort[256];
    private bool _hasData;

    // Cached summary values
    public double RedGamma { get; private set; } = 1.0;
    public double GreenGamma { get; private set; } = 1.0;
    public double BlueGamma { get; private set; } = 1.0;
    public double EstimatedColorTemp { get; private set; } = 6500;
    public double BlackPointPercent { get; private set; }
    public double WhitePointPercent { get; private set; } = 100.0;

    /// <summary>
    /// Event raised when ramp data is updated, so the parent can refresh info text.
    /// </summary>
    public event EventHandler? RampUpdated;

    #endregion

    #region Colors

    private static readonly Color GridColor = Color.FromRgb(0x2E, 0x2E, 0x34);
    private static readonly Color GridColorSubtle = Color.FromRgb(0x24, 0x24, 0x28);
    private static readonly Color IdentityColor = Color.FromRgb(0x3E, 0x3E, 0x44);
    private static readonly Color RedColor = Color.FromRgb(0xFF, 0x6B, 0x6B);
    private static readonly Color GreenColor = Color.FromRgb(0x51, 0xCF, 0x66);
    private static readonly Color BlueColor = Color.FromRgb(0x33, 0x9A, 0xF0);
    private static readonly Color BackgroundColor = Color.FromArgb(0,0,0,0);
    private static readonly Color PlotBackgroundColor = Color.FromRgb(30,30,30);
    private static readonly Color LabelColor = Color.FromRgb(0x5A, 0x5A, 0x64);

    #endregion

    static GammaRampDisplay()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GammaRampDisplay), new FrameworkPropertyMetadata(typeof(GammaRampDisplay)));
    }

    public GammaRampDisplay()
    {
        // Initialize with identity ramp
        for (int i = 0; i < 256; i++)
        {
            _red[i] = (ushort)(i * 256);
            _green[i] = (ushort)(i * 256);
            _blue[i] = (ushort)(i * 256);
        }

        ClipToBounds = true;
    }

    /// <summary>
    /// Update the display with new gamma ramp data.
    /// Call this after every ApplyColorProfile / SetDeviceGammaRamp.
    /// </summary>
    public void UpdateRamp(ushort[] red, ushort[] green, ushort[] blue)
    {
        if (red.Length != 256 || green.Length != 256 || blue.Length != 256)
            return;

        Array.Copy(red, _red, 256);
        Array.Copy(green, _green, 256);
        Array.Copy(blue, _blue, 256);
        _hasData = true;

        CalculateSummary();
        InvalidateVisual();
        RampUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Update from a GammaRamp struct directly.
    /// </summary>
    public void UpdateRamp(Native.NativeMethods.GammaRamp ramp)
    {
        UpdateRamp(ramp.Red, ramp.Green, ramp.Blue);
    }

    /// <summary>
    /// Reset to identity (diagonal line).
    /// </summary>
    public void ResetToIdentity()
    {
        for (int i = 0; i < 256; i++)
        {
            _red[i] = (ushort)(i * 256);
            _green[i] = (ushort)(i * 256);
            _blue[i] = (ushort)(i * 256);
        }
        _hasData = false;
        CalculateSummary();
        InvalidateVisual();
        RampUpdated?.Invoke(this, EventArgs.Empty);
    }

    #region Summary Calculations

    private void CalculateSummary()
    {
        RedGamma = EstimateGamma(_red);
        GreenGamma = EstimateGamma(_green);
        BlueGamma = EstimateGamma(_blue);

        // Estimate color temperature from RGB balance at midpoint
        double rMid = _red[128] / 65535.0;
        double gMid = _green[128] / 65535.0;
        double bMid = _blue[128] / 65535.0;
        EstimatedColorTemp = EstimateColorTemp(rMid, gMid, bMid);

        // Black point: first output value as percentage of max
        BlackPointPercent = Math.Max(Math.Max(_red[0], _green[0]), _blue[0]) / 65535.0 * 100.0;

        // White point: last output value as percentage of max
        WhitePointPercent = Math.Min(Math.Min(_red[255], _green[255]), _blue[255]) / 65535.0 * 100.0;
    }

    private static double EstimateGamma(ushort[] channel)
    {
        // Use midpoint to estimate effective gamma
        // For identity: output[128] = 128*256 = 32768 = 50% of 65535
        // gamma = log(output/max) / log(input/max)
        double input = 128.0 / 255.0;
        double output = channel[128] / 65535.0;

        if (output <= 0.001 || output >= 0.999 || input <= 0.001)
            return 1.0;

        return Math.Log(output) / Math.Log(input);
    }

    private static double EstimateColorTemp(double r, double g, double b)
    {
        // Simple heuristic: warm light has high R, low B
        if (b < 0.001) return 2000;
        double ratio = r / b;

        // Map ratio to approximate Kelvin
        // ratio > 1 = warm (low K), ratio < 1 = cool (high K)
        if (ratio > 1.5) return 3000;
        if (ratio > 1.3) return 3500;
        if (ratio > 1.1) return 4500;
        if (ratio > 0.95) return 5500;
        if (ratio > 0.85) return 6500;
        if (ratio > 0.75) return 7500;
        if (ratio > 0.6) return 9000;
        return 10000;
    }

    #endregion

    #region Rendering

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 10) return;

        // Padding for axis labels
        const double padLeft = 12;
        const double padBottom = 12;
        const double padTop = 3;
        const double padRight = 3;

        double plotW = w - padLeft - padRight;
        double plotH = h - padTop - padBottom;

        // Background (full control area)
        dc.DrawRectangle(new SolidColorBrush(BackgroundColor), null, new Rect(0, 0, w, h));

        // Plot area fill (darker than control background)
        var plotBgBrush = new SolidColorBrush(PlotBackgroundColor);
        plotBgBrush.Freeze();
        dc.DrawRectangle(plotBgBrush, null, new Rect(padLeft, padTop, plotW, plotH));

        // Plot area border
        var borderPen = new Pen(new SolidColorBrush(GridColor), 1);
        borderPen.Freeze();
        dc.DrawRectangle(null, borderPen, new Rect(padLeft, padTop, plotW, plotH));

        // Grid lines (4x4 grid = lines at 25%, 50%, 75%)
        var gridPen = new Pen(new SolidColorBrush(GridColorSubtle), 0.5);
        gridPen.Freeze();

        for (int g = 1; g <= 3; g++)
        {
            double fracX = padLeft + (plotW * g / 4.0);
            double fracY = padTop + (plotH * g / 4.0);

            dc.DrawLine(gridPen, new Point(fracX, padTop), new Point(fracX, padTop + plotH));
            dc.DrawLine(gridPen, new Point(padLeft, fracY), new Point(padLeft + plotW, fracY));
        }

        // Identity line (diagonal)
        if (ShowIdentity)
        {
            var identityPen = new Pen(new SolidColorBrush(IdentityColor), 1);
            identityPen.DashStyle = DashStyles.Dash;
            identityPen.Freeze();
            dc.DrawLine(identityPen, new Point(padLeft, padTop + plotH), new Point(padLeft + plotW, padTop));
        }

        // Draw curves
        if (ShowRed) DrawCurve(dc, _red, RedColor, padLeft, padTop, plotW, plotH);
        if (ShowGreen) DrawCurve(dc, _green, GreenColor, padLeft, padTop, plotW, plotH);
        if (ShowBlue) DrawCurve(dc, _blue, BlueColor, padLeft, padTop, plotW, plotH);

        // Axis labels
        var labelBrush = new SolidColorBrush(LabelColor);
        labelBrush.Freeze();
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // Y-axis labels
        DrawText(dc, "0", typeface, 8, labelBrush, padLeft - 4, padTop + plotH - 4, FlowDirection.RightToLeft);
        DrawText(dc, "½", typeface, 8, labelBrush, padLeft - 4, padTop + plotH / 2 - 4, FlowDirection.RightToLeft);
        DrawText(dc, "1", typeface, 8, labelBrush, padLeft - 4, padTop - 2, FlowDirection.RightToLeft);

        // X-axis labels
        DrawText(dc, "0", typeface, 8, labelBrush, padLeft - 2, padTop + plotH + 3, FlowDirection.LeftToRight);
        DrawText(dc, "128", typeface, 8, labelBrush, padLeft + plotW / 2 - 8, padTop + plotH + 3, FlowDirection.LeftToRight);
        DrawText(dc, "255", typeface, 8, labelBrush, padLeft + plotW - 12, padTop + plotH + 3, FlowDirection.LeftToRight);

        // "No data" overlay
        if (!_hasData)
        {
            var noDataText = new FormattedText("No Ramp Data",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 11,
                new SolidColorBrush(LabelColor),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(noDataText, new Point(
                w / 2 - noDataText.Width / 2,
                h / 2 - noDataText.Height / 2));
        }
    }

    private void DrawCurve(DrawingContext dc, ushort[] data, Color color,
        double padLeft, double padTop, double plotW, double plotH)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.5);
        pen.Freeze();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool started = false;

            // Sample every point for smooth curves (256 points is fine for performance)
            for (int i = 0; i < 256; i++)
            {
                double x = padLeft + (i / 255.0) * plotW;
                double y = padTop + plotH - (data[i] / 65535.0) * plotH;

                if (!started)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                    started = true;
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, true);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawText(DrawingContext dc, string text, Typeface typeface,
        double size, Brush brush, double x, double y, FlowDirection flow)
    {
        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            flow, typeface, size, brush, 96);
        dc.DrawText(ft, new Point(x, y));
    }

    #endregion
}