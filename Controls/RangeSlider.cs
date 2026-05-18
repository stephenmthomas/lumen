using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DisplayControl.Controls;

/// <summary>
/// A two-thumb range slider for selecting a min/max range.
/// 
/// Template parts:
///   PART_Track           – the full-width background track
///   PART_RangeFill       – the filled region between the two thumbs
///   PART_LowThumb        – the left (minimum) thumb
///   PART_HighThumb       – the right (maximum) thumb
///
/// Usage:
///   &lt;local:RangeSlider Minimum="0" Maximum="255"
///                       RangeMin="30" RangeMax="220" /&gt;
/// </summary>
[TemplatePart(Name = "PART_Track",     Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_RangeFill", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_LowThumb",  Type = typeof(Thumb))]
[TemplatePart(Name = "PART_HighThumb", Type = typeof(Thumb))]
public class RangeSlider : Control
{
    #region Dependency Properties

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange,
                OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsArrange,
                OnRangeChanged));

    public static readonly DependencyProperty RangeMinProperty =
        DependencyProperty.Register(nameof(RangeMin), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnRangeChanged, CoerceRangeMin));

    public static readonly DependencyProperty RangeMaxProperty =
        DependencyProperty.Register(nameof(RangeMax), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(100.0,
                FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnRangeChanged, CoerceRangeMax));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(RangeSlider),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RangeSlider),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RangeFillBrushProperty =
        DependencyProperty.Register(nameof(RangeFillBrush), typeof(Brush), typeof(RangeSlider),
            new PropertyMetadata(null));

    /// <summary>The absolute minimum of the slider scale.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The absolute maximum of the slider scale.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The selected lower value (left thumb).</summary>
    public double RangeMin
    {
        get => (double)GetValue(RangeMinProperty);
        set => SetValue(RangeMinProperty, value);
    }

    /// <summary>The selected upper value (right thumb).</summary>
    public double RangeMax
    {
        get => (double)GetValue(RangeMaxProperty);
        set => SetValue(RangeMaxProperty, value);
    }

    /// <summary>
    /// Optional snap step. When > 0, thumb values snap to multiples.
    /// Set to 0 (default) for continuous movement.
    /// </summary>
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>Optional override for the background track brush.</summary>
    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Optional override for the filled range brush.</summary>
    public Brush RangeFillBrush
    {
        get => (Brush)GetValue(RangeFillBrushProperty);
        set => SetValue(RangeFillBrushProperty, value);
    }

    #endregion

    #region Routed Events

    public static readonly RoutedEvent RangeChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(RangeChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(RangeSlider));

    /// <summary>Raised whenever RangeMin or RangeMax changes.</summary>
    public event RoutedEventHandler RangeChanged
    {
        add => AddHandler(RangeChangedEvent, value);
        remove => RemoveHandler(RangeChangedEvent, value);
    }

    #endregion

    #region Template Parts

    private FrameworkElement? _track;
    private FrameworkElement? _rangeFill;
    private Thumb? _lowThumb;
    private Thumb? _highThumb;

    #endregion

    static RangeSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RangeSlider),
            new FrameworkPropertyMetadata(typeof(RangeSlider)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Unhook previous thumbs
        if (_lowThumb != null)
            _lowThumb.DragDelta -= OnLowThumbDragDelta;
        if (_highThumb != null)
            _highThumb.DragDelta -= OnHighThumbDragDelta;

        _track     = GetTemplateChild("PART_Track")     as FrameworkElement;
        _rangeFill = GetTemplateChild("PART_RangeFill") as FrameworkElement;
        _lowThumb  = GetTemplateChild("PART_LowThumb")  as Thumb;
        _highThumb = GetTemplateChild("PART_HighThumb") as Thumb;

        if (_lowThumb != null)
            _lowThumb.DragDelta += OnLowThumbDragDelta;
        if (_highThumb != null)
            _highThumb.DragDelta += OnHighThumbDragDelta;

        // Allow click-on-track to move nearest thumb
        if (_track != null)
            _track.MouseLeftButtonDown += OnTrackMouseDown;

        UpdateVisuals();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateVisuals();
    }

    #region Drag Handlers

    private void OnLowThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_track == null) return;

        double trackWidth = _track.ActualWidth;
        if (trackWidth <= 0) return;

        double range = Maximum - Minimum;
        double deltaValue = (e.HorizontalChange / trackWidth) * range;
        double newValue = RangeMin + deltaValue;

        newValue = Snap(newValue);
        newValue = Clamp(newValue, Minimum, RangeMax);

        RangeMin = newValue;
    }

    private void OnHighThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_track == null) return;

        double trackWidth = _track.ActualWidth;
        if (trackWidth <= 0) return;

        double range = Maximum - Minimum;
        double deltaValue = (e.HorizontalChange / trackWidth) * range;
        double newValue = RangeMax + deltaValue;

        newValue = Snap(newValue);
        newValue = Clamp(newValue, RangeMin, Maximum);

        RangeMax = newValue;
    }

    private void OnTrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_track == null) return;

        Point pos = e.GetPosition(_track);
        double trackWidth = _track.ActualWidth;
        if (trackWidth <= 0) return;

        double ratio = pos.X / trackWidth;
        double clickValue = Minimum + ratio * (Maximum - Minimum);
        clickValue = Snap(clickValue);

        // Move whichever thumb is closer
        double distToLow  = Math.Abs(clickValue - RangeMin);
        double distToHigh = Math.Abs(clickValue - RangeMax);

        if (distToLow <= distToHigh)
            RangeMin = Clamp(clickValue, Minimum, RangeMax);
        else
            RangeMax = Clamp(clickValue, RangeMin, Maximum);
    }

    #endregion

    #region Layout / Visuals

    private void UpdateVisuals()
    {
        if (_track == null || _rangeFill == null || _lowThumb == null || _highThumb == null)
            return;

        double trackWidth = _track.ActualWidth;
        if (trackWidth <= 0) return;

        double range = Maximum - Minimum;
        if (range <= 0) return;

        double lowRatio  = (RangeMin - Minimum) / range;
        double highRatio = (RangeMax - Minimum) / range;

        double thumbHalf = _lowThumb.ActualWidth > 0 ? _lowThumb.ActualWidth / 2.0 : 9.0;

        // Position low thumb
        double lowLeft = lowRatio * trackWidth - thumbHalf;
        Canvas.SetLeft(_lowThumb, lowLeft);

        // Position high thumb
        double highLeft = highRatio * trackWidth - thumbHalf;
        Canvas.SetLeft(_highThumb, highLeft);

        // Position and size the filled range bar
        double fillLeft  = lowRatio * trackWidth;
        double fillWidth = (highRatio - lowRatio) * trackWidth;

        Canvas.SetLeft(_rangeFill, fillLeft);
        _rangeFill.Width = Math.Max(0, fillWidth);
    }

    #endregion

    #region Coercion & Callbacks

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeSlider slider)
        {
            // Re-coerce both values to enforce constraints
            slider.CoerceValue(RangeMinProperty);
            slider.CoerceValue(RangeMaxProperty);
            slider.UpdateVisuals();
            slider.RaiseEvent(new RoutedEventArgs(RangeChangedEvent));
        }
    }

    private static object CoerceRangeMin(DependencyObject d, object baseValue)
    {
        var slider = (RangeSlider)d;
        double val = (double)baseValue;
        val = Math.Max(val, slider.Minimum);
        val = Math.Min(val, slider.RangeMax);
        return val;
    }

    private static object CoerceRangeMax(DependencyObject d, object baseValue)
    {
        var slider = (RangeSlider)d;
        double val = (double)baseValue;
        val = Math.Min(val, slider.Maximum);
        val = Math.Max(val, slider.RangeMin);
        return val;
    }

    private double Snap(double value)
    {
        if (Step > 0)
            return Math.Round(value / Step) * Step;
        return value;
    }

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(max, value));

    #endregion
}
