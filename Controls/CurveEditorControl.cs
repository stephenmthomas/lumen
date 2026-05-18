using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DisplayControl.Services;

namespace DisplayControl.Controls;

/// <summary>
/// Interactive curve editor control similar to Photoshop/Lightroom curves.
/// Supports dragging control points, adding/removing points, and displays
/// a smooth cubic spline curve through all points.
/// </summary>
public class CurveEditorControl : Control
{
    #region Dependency Properties

    public static readonly DependencyProperty CurveProperty =
        DependencyProperty.Register(nameof(Curve), typeof(ToneCurve), typeof(CurveEditorControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnCurveChanged));

    public static readonly DependencyProperty CurveColorProperty =
        DependencyProperty.Register(nameof(CurveColor), typeof(Brush), typeof(CurveEditorControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridColorProperty =
        DependencyProperty.Register(nameof(GridColor), typeof(Brush), typeof(CurveEditorControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(60, 60, 60)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridMinorColorProperty =
    DependencyProperty.Register(nameof(GridMinorColor), typeof(Brush), typeof(CurveEditorControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(40, 40, 40)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PointRadiusProperty =
        DependencyProperty.Register(nameof(PointRadius), typeof(double), typeof(CurveEditorControl),
            new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(CurveEditorControl),
            new PropertyMetadata(false));

    public ToneCurve? Curve
    {
        get => (ToneCurve?)GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public Brush CurveColor
    {
        get => (Brush)GetValue(CurveColorProperty);
        set => SetValue(CurveColorProperty, value);
    }

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

    public double PointRadius
    {
        get => (double)GetValue(PointRadiusProperty);
        set => SetValue(PointRadiusProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    #endregion

    #region Events

    public event EventHandler? CurveChanged;

    #endregion

    private int _dragIndex = -1;
    private const double HitTestRadius = 8.0;
    private const double Padding = 0.0;

    static CurveEditorControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CurveEditorControl),
            new FrameworkPropertyMetadata(typeof(CurveEditorControl)));
    }

    public CurveEditorControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        MinHeight = 150;
        MinWidth = 150;
        ClipToBounds = true;
    }

    private static void OnCurveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CurveEditorControl editor)
            editor.InvalidateVisual();
    }

    #region Coordinate Transforms

    private Point CurveToScreen(int input, int output)
    {
        double w = ActualWidth - 2 * Padding;
        double h = ActualHeight - 2 * Padding;
        double x = Padding + (input / 255.0) * w;
        double y = Padding + (1.0 - output / 255.0) * h;
        return new Point(x, y);
    }

    private (int input, int output) ScreenToCurve(Point pt)
    {
        double w = ActualWidth - 2 * Padding;
        double h = ActualHeight - 2 * Padding;
        int input = (int)Math.Clamp((pt.X - Padding) / w * 255, 0, 255);
        int output = (int)Math.Clamp((1.0 - (pt.Y - Padding) / h) * 255, 0, 255);
        return (input, output);
    }

    #endregion

    #region Rendering

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 1 || h < 1) return;

        // Background
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));

        var gridPen = new Pen(GridColor, 0.5);
        var minorPen = new Pen(GridMinorColor, 0.5);
        gridPen.Freeze();

        // Grid lines (4x4)
        for (int i = 1; i < 4; i++)
        {
            double frac = i / 4.0;
            double gx = Padding + frac * (w - 2 * Padding);
            double gy = Padding + frac * (h - 2 * Padding);
            dc.DrawLine(gridPen, new Point(gx, Padding), new Point(gx, h - Padding));
            dc.DrawLine(gridPen, new Point(Padding, gy), new Point(w - Padding, gy));
        }

        // MINOR lines (4x4)
        for (int i = 1; i < 5; i++)
        {
            double minorFrac = (i - 0.5) / 4.0;
            double mx = Padding + minorFrac * (w - 2 * Padding);
            double my = Padding + minorFrac * (h - 2 * Padding);
            dc.DrawLine(minorPen, new Point(mx, Padding), new Point(mx, h - Padding));
            dc.DrawLine(minorPen, new Point(Padding, my), new Point(w - Padding, my));
        }

        // Border
        var borderPen = new Pen(GridColor, 1.0);
        borderPen.Freeze();
        dc.DrawRectangle(null, borderPen, new Rect(Padding, Padding, w - 2 * Padding, h - 2 * Padding));

        // Identity line (diagonal)
        var identityPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1.0);
        identityPen.Freeze();
        dc.DrawLine(identityPen, CurveToScreen(0, 0), CurveToScreen(255, 255));

        if (Curve == null || Curve.Points.Count < 2) return;

        // Draw the interpolated curve
        var curvePen = new Pen(CurveColor, 2.0);
        curvePen.Freeze();

        var sorted = Curve.Points.OrderBy(p => p.Input).ToList();
        var spline = new MonotoneCubicSpline(sorted);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = CurveToScreen(0, (int)Math.Clamp(spline.Evaluate(0), 0, 255));
            ctx.BeginFigure(first, false, false);

            for (int i = 1; i <= 255; i++)
            {
                double yVal = Math.Clamp(spline.Evaluate(i), 0, 255);
                ctx.LineTo(CurveToScreen(i, (int)yVal), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, curvePen, geometry);

        // Draw control points
        double r = PointRadius;
        var pointFill = CurveColor.Clone();
        pointFill.Freeze();
        var pointBorder = new Pen(Brushes.White, 1.5);
        pointBorder.Freeze();
        var pointBorderDark = new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 1.0);
        pointBorderDark.Freeze();

        foreach (var pt in sorted)
        {
            var screenPt = CurveToScreen(pt.Input, pt.Output);
            dc.DrawEllipse(pointFill, pointBorder, screenPt, r, r);
        }
    }

    #endregion

    #region Mouse Interaction

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (IsReadOnly || Curve == null) return;

        var pos = e.GetPosition(this);
        var sorted = Curve.Points.OrderBy(p => p.Input).ToList();

        // Check if clicking on an existing point
        for (int i = 0; i < sorted.Count; i++)
        {
            var screenPt = CurveToScreen(sorted[i].Input, sorted[i].Output);
            if ((pos - screenPt).Length < HitTestRadius)
            {
                _dragIndex = Curve.Points.IndexOf(sorted[i]);
                CaptureMouse();
                return;
            }
        }

        // Double-click to add a new point
        if (e.ClickCount == 1)
        {
            var (input, output) = ScreenToCurve(pos);
            // Don't add too close to existing points
            bool tooClose = sorted.Any(p => Math.Abs(p.Input - input) < 8);
            if (!tooClose)
            {
                var newPoint = new CurvePoint(input, output);
                Curve.Points.Add(newPoint);
                _dragIndex = Curve.Points.Count - 1;
                CaptureMouse();
                InvalidateVisual();
                CurveChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsReadOnly || Curve == null || _dragIndex < 0) return;

        // SAFETY: Check if drag index is still valid
        if (_dragIndex >= Curve.Points.Count)
        {
            _dragIndex = -1;  // Cancel drag
            return;
        }

        var pos = e.GetPosition(this);
        var (input, output) = ScreenToCurve(pos);

        var point = Curve.Points[_dragIndex];

        // Lock first and last points horizontally
        var sorted = Curve.Points.OrderBy(p => p.Input).ToList();
        bool isFirst = sorted.First() == point;
        bool isLast = sorted.Last() == point;

        if (isFirst)
            point.Input = 0;
        else if (isLast)
            point.Input = 255;
        else
            point.Input = input;

        point.Output = output;

        InvalidateVisual();
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        // Always clear drag state on left button up
        _dragIndex = -1;
        ReleaseMouseCapture();

        e.Handled = true;
    }

    protected void OnMouseLeftButtonUp2(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (IsReadOnly || Curve == null) return;

        // If dragging, cancel the drag before removing point
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            ReleaseMouseCapture();
        }

        var pos = e.GetPosition(this);
        var (input, output) = ScreenToCurve(pos);

        // Find closest point
        var closest = Curve.Points
            .Select((p, i) => new { Point = p, Index = i })
            .OrderBy(x => Math.Abs(x.Point.Input - input) + Math.Abs(x.Point.Output - output))
            .FirstOrDefault();

        if (closest != null)
        {
            var screenDist = Math.Sqrt(
                Math.Pow(CurveToScreen(closest.Point.Input, closest.Point.Output).X - pos.X, 2) +
                Math.Pow(CurveToScreen(closest.Point.Input, closest.Point.Output).Y - pos.Y, 2)
            );

            if (screenDist < 10 && Curve.Points.Count > 2)
            {
                Curve.Points.RemoveAt(closest.Index);
                InvalidateVisual();
                CurveChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        e.Handled = true;
    }

    #endregion

    /// <summary>
    /// Resets the curve to identity (straight line).
    /// </summary>
    public void ResetCurve()
    {
        if (Curve == null) return;
        Curve.Points.Clear();
        Curve.Points.Add(new CurvePoint(0, 0));
        Curve.Points.Add(new CurvePoint(255, 255));
        InvalidateVisual();
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }
}
