using DisplayControl.Native;
using DisplayControl.Services;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using static DisplayControl.SettingsWindow;

namespace DisplayControl;

public partial class SettingsWindow : Window
{
    private bool _allowNativeChromeMessages = false; //to allow dialog intercepts, modals, etc...

    private readonly DisplayService _displayService;
    private bool _isUpdating;
    private ColorProfile _currentProfile;

    private FilterService? _filterService;
    private FilterProfile _currentFilterProfile = new();
    private bool _filtersEnabled;

    private readonly ICCProfileService _iccProfileService;

    private SettingsService? _settingsService;

    public string _version = "0.0.1";

    private bool _profileDirty = false;
    private List<UserPreset> _userPresets = new();

    public SettingsWindow(DisplayService displayService, FilterService filterService, SettingsService settingsService, ICCProfileService iccProfileService)
    {
        InitializeComponent();

        _displayService = displayService;
        _displayService.SettingsWindow = this;
        _filterService = filterService;
        _settingsService = settingsService;
        _iccProfileService = iccProfileService;
        _currentProfile = new ColorProfile();

        LoadMonitors();
        InitializeCurveEditors();
        LoadCurrentValues();
        CheckSystemFilters();
        UpdateHistogram();
        LoadICCProfiles();
        LoadUserPresets();

        SourceInitialized += (_, _) =>
        {
            ApplyDwm();
        };

        StartMinimizedCheckbox.IsChecked = _settingsService.Settings.StartMinimized;
        ApplyRealTimeCheckbox.IsChecked = _settingsService.Settings.RealTimeUpdates;
        RealTimeHistogramCheckbox.IsChecked = _settingsService.Settings.RealTimeHistogram;
        MinimizeOnCloseCheckbox.IsChecked = _settingsService.Settings.MinimizeOnClose;
        ResetOnExitCheckbox.IsChecked = _settingsService.Settings.ResetOnExit;
        AlwaysOnTopCheckbox.IsChecked = _settingsService.Settings.AlwaysOnTop;

        this.Topmost = _settingsService.Settings.AlwaysOnTop;

        VersionTextBlock.Text = "Version " + _version;

        UpdateStatus("Ready...", StatusType.Good);

        CheckGammaRampStatus();
    }

    #region Initialization

    private void LoadMonitors()
    {
        var monitors = _displayService.GetMonitors();
        MonitorComboBox.Items.Clear();

        MonitorComboBox.Items.Add(new ComboBoxItem
        {
            Content = "All Monitors",
            Tag = null
        });

        foreach (var monitor in monitors)
        {
            var displayName = monitor.IsPrimary
                ? $"{monitor.DeviceName} (Primary)"
                : monitor.DeviceName;

            MonitorComboBox.Items.Add(new ComboBoxItem
            {
                Content = displayName,
                Tag = monitor.DeviceName
            });
        }

        MonitorComboBox.SelectedIndex = 0;
    }

    private void InitializeCurveEditors()
    {
        // Bind curve objects to editors
        MasterCurveEditor.Curve = _currentProfile.Curve;
        RedCurveEditor.Curve = _currentProfile.RedCurve;
        GreenCurveEditor.Curve = _currentProfile.GreenCurve;
        BlueCurveEditor.Curve = _currentProfile.BlueCurve;
    }

    private void UpdateHistogram()
    {

        // Capture screen (downsampled for performance)
        var (pixels, width, height, stride) = ScreenCapture.CapturePrimaryScreenDownsampled(640, 360);

        // Feed to histogram
        HistogramDisplay.SetFromPixelData(pixels, stride, width, height);
    }

    private void LoadCurrentValues()
    {
        _isUpdating = true;

        // Exposure
        ExposureSlider.Value = 0;
        OffsetSlider.Value = 0;
        GammaSlider.Value = 100;

        // Tone
        BrightnessSlider.Value = 100;
        ContrastSlider.Value = 100;

        // Tonal Zones
        BlacksSlider.Value = 0;
        ShadowsSlider.Value = 0;
        MidtonesSlider.Value = 0;
        HighlightsSlider.Value = 0;
        WhitesSlider.Value = 0;

        // Saturation
        VibranceSlider.Value = 0;
        SaturationSlider.Value = 100;

        // Color
        ColorTempSlider.Value = 6500;
        TintSlider.Value = 0;
        RedGainSlider.Value = 100;
        GreenGainSlider.Value = 100;
        BlueGainSlider.Value = 100;

        // Levels
        LevelsEnabledCheckbox.IsChecked = false;
        InputBlackSlider.Value = 0;
        InputWhiteSlider.Value = 255;
        OutputBlackSlider.Value = 0;
        OutputWhiteSlider.Value = 255;

        // Curves
        MasterCurveEnabled.IsChecked = false;
        ChannelCurvesEnabled.IsChecked = false;

        // Enhancers
        DynamicContrastSlider.Value = 0;
        BlueLightSlider.Value = 0;
        BlackEqualizerSlider.Value = 0;
        WhiteEqualizerSlider.Value = 0;

        FilterPresetComboBox.SelectedIndex = 0;

        _isUpdating = false;
    }

    #endregion

    #region Monitor Selection

    private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadICCProfiles();
    }

    private string? GetSelectedMonitor()
    {
        if (MonitorComboBox.SelectedItem is ComboBoxItem item)
            return item.Tag as string;
        return null;
    }

    #endregion

    #region Exposure Handlers

    private void ExposureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Exposure = e.NewValue / 100.0;  // -3.0 to +3.0
        ApplyCurrentProfile();
    }

    private void OffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Offset = e.NewValue / 200.0;  // -0.5 to +0.5
        ApplyCurrentProfile();
    }

    private void GammaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Gamma = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    #endregion

    #region Tone Handlers

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Brightness = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void ContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Contrast = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    #endregion

    #region Tonal Zone Handlers

    private void BlacksSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Blacks = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void ShadowsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Shadows = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void MidtonesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Midtones = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void HighlightsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Highlights = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void WhitesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Whites = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    #endregion

    #region Saturation Handlers

    private void VibranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Vibrance = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void SaturationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Saturation = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    #endregion

    #region Color Handlers

    private void ColorTempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.ColorTemperature = (int)e.NewValue;
        ApplyCurrentProfile();
    }

    private void TintSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.Tint = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void RedGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.RedGain = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void GreenGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.GreenGain = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void BlueGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.BlueGain = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    #endregion

    #region Levels Handlers

    private void LevelsEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;

        bool isEnabled = LevelsEnabledCheckbox.IsChecked == true;

        _currentProfile.UseLevels = isEnabled;
        LevelsPanel.IsEnabled = isEnabled;
        LevelsPanel.Opacity = isEnabled ? 1.0 : 0.75;

        ApplyCurrentProfile();
    }

    private void Levels_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _currentProfile == null) return;

        _currentProfile.Levels.InputBlack = (byte)InputBlackSlider.Value;
        _currentProfile.Levels.InputWhite = (byte)InputWhiteSlider.Value;
        _currentProfile.Levels.OutputBlack = (byte)OutputBlackSlider.Value;
        _currentProfile.Levels.OutputWhite = (byte)OutputWhiteSlider.Value;

        if (_currentProfile.UseLevels)
            ApplyCurrentProfile();
    }

    #endregion

    #region Curve Handlers

    private void MasterCurveEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;
        _currentProfile.UseCurve = MasterCurveEnabled.IsChecked == true;
        ApplyCurrentProfile();
    }

    private void MasterCurve_Changed(object? sender, EventArgs e)
    {
        if (_isUpdating) return;
        if (_currentProfile.UseCurve)
            ApplyCurrentProfile();
    }

    private void ChannelCurvesEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;
        _currentProfile.UseChannelCurves = ChannelCurvesEnabled.IsChecked == true;
        ApplyCurrentProfile();
    }

    private void ChannelCurve_Changed(object? sender, EventArgs e)
    {
        if (_isUpdating) return;
        if (_currentProfile.UseChannelCurves)
            ApplyCurrentProfile();
    }

    private void ResetMasterCurve_Click(object sender, RoutedEventArgs e)
    {
        MasterCurveEditor.ResetCurve();
    }

    private void ResetChannelCurves_Click(object sender, RoutedEventArgs e)
    {
        RedCurveEditor.ResetCurve();
        GreenCurveEditor.ResetCurve();
        BlueCurveEditor.ResetCurve();
    }

    #endregion

    #region Enhancer Handlers

    private void DynamicContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.DynamicContrast = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void BlueLightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.BlueLightFilter = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void BlackEqualizerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.BlackEqualizer = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    private void WhiteEqualizerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _displayService == null) return;
        _currentProfile.WhiteEqualizer = e.NewValue / 100.0;
        ApplyCurrentProfile();
    }

    #endregion

    #region Presets

    private void DefaultPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset(ColorProfile.Default);
    private void NightPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset(ColorProfile.Night);
    private void ReadingPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset(ColorProfile.Reading);
    private void GamingPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset(ColorProfile.Gaming);

    private void ApplyPreset(ColorProfile profile)
    {
        _isUpdating = true;

        // Update current profile
        _currentProfile = profile.Clone();

        // Exposure
        ExposureSlider.Value = profile.Exposure * 100;
        OffsetSlider.Value = profile.Offset * 200;
        GammaSlider.Value = profile.Gamma * 100;

        // Tone
        BrightnessSlider.Value = profile.Brightness * 100;
        ContrastSlider.Value = profile.Contrast * 100;

        // Tonal Zones
        BlacksSlider.Value = profile.Blacks * 100;
        ShadowsSlider.Value = profile.Shadows * 100;
        MidtonesSlider.Value = profile.Midtones * 100;
        HighlightsSlider.Value = profile.Highlights * 100;
        WhitesSlider.Value = profile.Whites * 100;

        // Saturation
        VibranceSlider.Value = profile.Vibrance * 100;
        SaturationSlider.Value = profile.Saturation * 100;

        // Color
        ColorTempSlider.Value = profile.ColorTemperature;
        TintSlider.Value = profile.Tint * 100;
        RedGainSlider.Value = profile.RedGain * 100;
        GreenGainSlider.Value = profile.GreenGain * 100;
        BlueGainSlider.Value = profile.BlueGain * 100;

        // Levels
        LevelsEnabledCheckbox.IsChecked = profile.UseLevels;
        InputBlackSlider.Value = profile.Levels.InputBlack;
        InputWhiteSlider.Value = profile.Levels.InputWhite;
        OutputBlackSlider.Value = profile.Levels.OutputBlack;
        OutputWhiteSlider.Value = profile.Levels.OutputWhite;

        // Curves
        MasterCurveEnabled.IsChecked = profile.UseCurve;
        ChannelCurvesEnabled.IsChecked = profile.UseChannelCurves;
        MasterCurveEditor.Curve = _currentProfile.Curve;
        RedCurveEditor.Curve = _currentProfile.RedCurve;
        GreenCurveEditor.Curve = _currentProfile.GreenCurve;
        BlueCurveEditor.Curve = _currentProfile.BlueCurve;
        MasterCurveEditor.InvalidateVisual();
        RedCurveEditor.InvalidateVisual();
        GreenCurveEditor.InvalidateVisual();
        BlueCurveEditor.InvalidateVisual();

        // Enhancers
        DynamicContrastSlider.Value = profile.DynamicContrast * 100;
        BlueLightSlider.Value = profile.BlueLightFilter * 100;
        BlackEqualizerSlider.Value = profile.BlackEqualizer * 100;
        WhiteEqualizerSlider.Value = profile.WhiteEqualizer * 100;

        _isUpdating = false;

        ApplyCurrentProfile();
    }

    #endregion

    #region Apply & Reset

    private void ApplyCurrentProfile()
    {
        // If real-time updates are disabled, mark dirty and skip
        if (!_settingsService.Settings.RealTimeUpdates)
        {
            _profileDirty = true;
            return;
        }

        _profileDirty = false;
        ApplyProfile();
    }

    private void ApplyProfile()
    {
        var selectedMonitor = GetSelectedMonitor();
        if (selectedMonitor == null)
            _displayService.ApplyColorProfile(_currentProfile);
        else
            _displayService.ApplyColorProfileToMonitor(selectedMonitor, _currentProfile);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var selectedMonitor = GetSelectedMonitor();
        if (selectedMonitor == null)
            _displayService.ResetAll();
        else
            _displayService.ResetMonitor(selectedMonitor);

        _currentProfile = new ColorProfile();
        InitializeCurveEditors();
        LoadCurrentValues();
    }

    #endregion

    #region Public Methods (for hotkey service)

    public void AdjustBrightness(double delta)
    {
        BrightnessSlider.Value = Math.Clamp(BrightnessSlider.Value + delta,
            BrightnessSlider.Minimum, BrightnessSlider.Maximum);
    }

    public void AdjustContrast(double delta)
    {
        ContrastSlider.Value = Math.Clamp(ContrastSlider.Value + delta,
            ContrastSlider.Minimum, ContrastSlider.Maximum);
    }

    public void AdjustGamma(double delta)
    {
        GammaSlider.Value = Math.Clamp(GammaSlider.Value + delta,
            GammaSlider.Minimum, GammaSlider.Maximum);
    }

    public void AdjustColorTemperature(double delta)
    {
        ColorTempSlider.Value = Math.Clamp(ColorTempSlider.Value + delta,
            ColorTempSlider.Minimum, ColorTempSlider.Maximum);
    }

    public void ResetToDefaults()
    {
        _displayService.ResetAll();
        _currentProfile = new ColorProfile();
        InitializeCurveEditors();
        LoadCurrentValues();
    }

    #endregion

    #region Filters
    // =================================================================
    // FILTERS TAB - Add these to SettingsWindow.xaml.cs
    // =================================================================

    private void CheckSystemFilters()
    {
        if (FilterService.AreSystemFiltersActive())
        {
            SystemFilterWarning.Visibility = Visibility.Visible;
        }
    }

    private void FilterEnabled_Changed(object sender, RoutedEventArgs e)
    {
        _filtersEnabled = FilterEnabledCheckbox.IsChecked == true;

        if (_filtersEnabled)
        {
            ApplyCurrentFilter();
        }
        else
        {
            _filterService?.ClearFilter();
        }
    }

    private void FilterPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating || FilterPresetComboBox == null) return;

        _currentFilterProfile.Preset = GetSelectedFilterPreset();
        ApplyCurrentFilter();
    }

    private FilterPreset GetSelectedFilterPreset()
    {
        if (FilterPresetComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return Enum.TryParse<FilterPreset>(tag, out var preset) ? preset : FilterPreset.None;
        return FilterPreset.None;
    }

    private void PresetStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.PresetStrength = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterInversionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.InversionStrength = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {

        if (_isUpdating || _filterService == null) return;

        _currentFilterProfile.Brightness = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterChannelOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;

        _currentFilterProfile.RedOffset = (float)(FilterRedOffsetSlider.Value / 100.0);
        _currentFilterProfile.GreenOffset = (float)(FilterGreenOffsetSlider.Value / 100.0);
        _currentFilterProfile.BlueOffset = (float)(FilterBlueOffsetSlider.Value / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.Contrast = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterSaturationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.Saturation = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterHueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.HueRotation = (float)e.NewValue;
        ApplyCurrentFilter();
    }

    private void FilterChannelGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.RedGain = (float)(FilterRedGainSlider.Value / 100.0);
        _currentFilterProfile.GreenGain = (float)(FilterGreenGainSlider.Value / 100.0);
        _currentFilterProfile.BlueGain = (float)(FilterBlueGainSlider.Value / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterBlackPointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.BlackPoint = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterWhitePointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.WhitePoint = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterVibranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.Vibrance = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterExposureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.Exposure = (float)(e.NewValue / 100.0);
        ApplyCurrentFilter();
    }

    private void FilterTemperatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || _filterService == null) return;
        _currentFilterProfile.Temperature = (float)e.NewValue;
        ApplyCurrentFilter();
    }

    private void ApplyCurrentFilter()
    {
        if (!_filtersEnabled || _filterService == null) return;

        _filterService.ApplyFilter(_currentFilterProfile);
        UpdateMatrixPreview();

        if (_settingsService.Settings.RealTimeHistogram) { UpdateHistogram(); }

    }

    private void UpdateMatrixPreview()
    {
        var matrix = _currentFilterProfile.BuildMatrix();

        // Row 0 (R output)
        M00.Text = matrix.M00.ToString("F3"); M01.Text = matrix.M01.ToString("F3");
        M02.Text = matrix.M02.ToString("F3"); M03.Text = matrix.M03.ToString("F3");
        M04.Text = matrix.M04.ToString("F3");

        // Row 1 (G output)
        M10.Text = matrix.M10.ToString("F3"); M11.Text = matrix.M11.ToString("F3");
        M12.Text = matrix.M12.ToString("F3"); M13.Text = matrix.M13.ToString("F3");
        M14.Text = matrix.M14.ToString("F3");

        // Row 2 (B output)
        M20.Text = matrix.M20.ToString("F3"); M21.Text = matrix.M21.ToString("F3");
        M22.Text = matrix.M22.ToString("F3"); M23.Text = matrix.M23.ToString("F3");
        M24.Text = matrix.M24.ToString("F3");

        // Row 3 (A output)
        M30.Text = matrix.M30.ToString("F3"); M31.Text = matrix.M31.ToString("F3");
        M32.Text = matrix.M32.ToString("F3"); M33.Text = matrix.M33.ToString("F3");
        M34.Text = matrix.M34.ToString("F3");

        // Row 4 (homogeneous coordinate) - ADDED
        M40.Text = matrix.M40.ToString("F3"); M41.Text = matrix.M41.ToString("F3");
        M42.Text = matrix.M42.ToString("F3"); M43.Text = matrix.M43.ToString("F3");
        M44.Text = matrix.M44.ToString("F3");
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _isUpdating = true;

        _currentFilterProfile = new FilterProfile();

        PresetStrengthSlider.Value = 100;
        FilterInversionSlider.Value = 0;
        FilterBrightnessSlider.Value = 0;
        FilterContrastSlider.Value = 100;
        FilterSaturationSlider.Value = 100;
        FilterHueSlider.Value = 0;
        FilterRedGainSlider.Value = 100;
        FilterGreenGainSlider.Value = 100;
        FilterBlueGainSlider.Value = 100;
        FilterRedOffsetSlider.Value = 0;
        FilterGreenOffsetSlider.Value = 0;
        FilterBlueOffsetSlider.Value = 0;

        FilterBlackPointSlider.Value = 0;
        FilterWhitePointSlider.Value = 100;
        FilterVibranceSlider.Value = 0;
        FilterExposureSlider.Value = 0;
        FilterTemperatureSlider.Value = 6500;

        _isUpdating = false;

        if (_filtersEnabled)
        {
            _filterService?.ClearFilter();
        }

        UpdateMatrixPreview();
    }

    private void UpdateFilterSliders()
    {
        PresetStrengthSlider.Value = _currentFilterProfile.PresetStrength * 100;
        FilterInversionSlider.Value = _currentFilterProfile.InversionStrength * 100;
        FilterBrightnessSlider.Value = _currentFilterProfile.Brightness * 100;
        FilterContrastSlider.Value = _currentFilterProfile.Contrast * 100;
        FilterSaturationSlider.Value = _currentFilterProfile.Saturation * 100;
        FilterHueSlider.Value = _currentFilterProfile.HueRotation;
        FilterRedGainSlider.Value = _currentFilterProfile.RedGain * 100;
        FilterGreenGainSlider.Value = _currentFilterProfile.GreenGain * 100;
        FilterBlueGainSlider.Value = _currentFilterProfile.BlueGain * 100;
        FilterRedOffsetSlider.Value = _currentFilterProfile.RedOffset * 100;
        FilterGreenOffsetSlider.Value = _currentFilterProfile.GreenOffset * 100;
        FilterBlueOffsetSlider.Value = _currentFilterProfile.BlueOffset * 100;
        FilterBlackPointSlider.Value = _currentFilterProfile.BlackPoint * 100;
        FilterWhitePointSlider.Value = _currentFilterProfile.WhitePoint * 100;
        FilterVibranceSlider.Value = _currentFilterProfile.Vibrance * 100;
        FilterExposureSlider.Value = _currentFilterProfile.Exposure * 100;
        FilterTemperatureSlider.Value = _currentFilterProfile.Temperature;
    }

    #endregion

    #region ICC Profiles
    private void LoadICCProfiles()
    {
        try
        {
            var selectedMonitor = GetSelectedMonitor();
            if (selectedMonitor == null)
            {
                ICCProfileComboBox.IsEnabled = false;
                ICCStatusText.Text = "Select a specific monitor (in the toolbar) to manage ICC profiles";
                return;
            }

            ICCProfileComboBox.IsEnabled = true;
            _isUpdating = true;
            ICCProfileComboBox.Items.Clear();

            var allProfiles = _iccProfileService.GetAllAvailableProfiles();

            if (allProfiles.Count == 0)
            {
                ICCStatusText.Text = "No ICC profiles found";
                return;
            }

            foreach (var profile in allProfiles)
            {
                ICCProfileComboBox.Items.Add(new ComboBoxItem { Content = profile, Tag = profile });
            }

            ICCProfileComboBox.SelectedIndex = 0;
            ICCStatusText.Text = $"{allProfiles.Count} profile(s) available";
        }
        catch (Exception ex)
        {
            ICCStatusText.Text = $"Error: {ex.Message}";
            ICCProfileComboBox.IsEnabled = false;
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void ICCProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Just track selection, don't apply yet
    }

    private async void ApplyICCProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ICCProfileComboBox.SelectedItem is not ComboBoxItem item) return;
        string profileName = item.Tag as string ?? "";

        var selectedMonitor = GetSelectedMonitor();
        if (selectedMonitor == null) return;

        // Show "working" state
        ICCStatusText.Text = $"Applying {profileName} to {GetSelectedMonitor()}";
        ICCStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 100));

        // Disable the entire ICC GroupBox
        ICCProfileGroupBox.IsEnabled = false;



        // Start color animation
        var timer = new System.Windows.Threading.DispatcherTimer();
        int dotCount = 0;
        timer.Interval = TimeSpan.FromMilliseconds(1000);
        timer.Tick += (s, args) =>
        {
            string dots = new string('.', dotCount % 4);
            ICCStatusText.Text = $"Applying {profileName} to {GetSelectedMonitor()}.{dots}";
            ICCStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 100));
            dotCount++;
        };
        timer.Start();

        bool success = await Task.Run(() => ApplyICCProfile(selectedMonitor, profileName));

        // Stop animation
        timer.Stop();

        // Re-enable controls
        ICCProfileGroupBox.IsEnabled = true;

        if (success)
        {
            ICCStatusText.Text = $"✓ Applied: {profileName}";
            ICCStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(100, 220, 100));

        }
        else
        {
            ICCStatusText.Text = $"✗ Failed to apply profile";
            ICCStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(220, 100, 100));
        }

    }

    private bool ApplyICCProfile(string Monitor, string Profile)
    {

        return _iccProfileService.SetProfileForMonitor(Monitor, Profile);

    }

    private void RefreshICCProfiles_Click(object sender, RoutedEventArgs e)
    {
        LoadICCProfiles();
    }

    private void OpenProfileFolder_Click(object sender, RoutedEventArgs e)
    {
        ICCProfileService.OpenColorProfileDirectory();
    }

    private void OpenDisplaySettings_Click(object sender, RoutedEventArgs e)
    {
        ICCProfileService.OpenDisplaySettings();
    }

    private void OpenColorManagement_Click(object sender, RoutedEventArgs e)
    {
        ICCProfileService.OpenColorManagement();
    }

    #endregion

    #region DWM

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool enabled);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;      // width of left border that retains its size
        public int cxRightWidth;     // width of right border that retains its size
        public int cyTopHeight;      // height of top border that retains its size
        public int cyBottomHeight;   // height of bottom border that retains its size
    };

    [DllImport("DwmApi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref MARGINS pMarInset);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    public enum DwmWindowCornerPreference
    {
        SystemDefault = 0,
        NoRounding = 1,
        Round = 2,
        MinorRounding = 3
    }

    public enum OpalSystemBackdrop
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        Acrylic = 3,
        MicaAlt = 4
    }

    private void ApplyDwm()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        DwmIsCompositionEnabled(out bool compositionEnabled);
        if (!compositionEnabled) return;

        //Set Dark Mode
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        //Border Color
        int borderColor = unchecked((int)0x00000000);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

        //Set Corner Preference
        int cornerPreference = (int)DwmWindowCornerPreference.Round;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        //Backdrop
        int backdrop = (int)OpalSystemBackdrop.Mica;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        // Set margins to extend the frame
        MARGINS margins = new MARGINS();
        margins.cxLeftWidth = 0;
        margins.cxRightWidth = 0;
        margins.cyTopHeight = 0; // Useful for custom title bars
        margins.cyBottomHeight = 0;

        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    #endregion

    #region hWnd IntPtr Resize

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private const int WM_SIZING = 0x0214;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCACTIVATE = 0x0086;

    private const int WM_NCHITTEST = 0x84;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private int _resizeHandleSize = 6;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            // Extract mouse coords from lParam
            int x = unchecked((short)((long)lParam & 0xFFFF));
            int y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
            var point = PointFromScreen(new Point(x, y));

            bool left = point.X <= _resizeHandleSize;
            bool right = point.X >= ActualWidth - _resizeHandleSize;
            bool top = point.Y <= _resizeHandleSize;
            bool bottom = point.Y >= ActualHeight - _resizeHandleSize;

            if (top && left) { handled = true; return (IntPtr)HTTOPLEFT; }
            if (top && right) { handled = true; return (IntPtr)HTTOPRIGHT; }
            if (bottom && left) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
            if (bottom && right) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
            if (left) { handled = true; return (IntPtr)HTLEFT; }
            if (right) { handled = true; return (IntPtr)HTRIGHT; }
            if (top) { handled = true; return (IntPtr)HTTOP; }
            if (bottom) { handled = true; return (IntPtr)HTBOTTOM; }
        }

        if (!_allowNativeChromeMessages)
        {
            if (msg == WM_NCACTIVATE)
            {
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_NCCALCSIZE && wParam.ToInt32() == 1)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source.AddHook(new HwndSourceHook(WndProc));
    }

    #endregion


    #region Form Code

    public void UpdateGammaRampControl(NativeMethods.GammaRamp ramp)
    {
        GammaRampView.UpdateRamp(ramp);
        if (_settingsService.Settings.RealTimeHistogram) { UpdateHistogram(); }
    }

    private void CheckGammaRampStatus()
    {
        var selectedMonitor = GetSelectedMonitor();
        if (selectedMonitor == null) selectedMonitor = _displayService.GetMonitors().FirstOrDefault()?.DeviceName;

        if (selectedMonitor != null)
        {
            string status = _displayService.GetGammaRampSummary(selectedMonitor);
            bool isModified = _displayService.IsGammaRampModified(selectedMonitor);
            bool isIdent = _displayService.DeviceHasIdentityRamp(selectedMonitor);

            // Show modified as warning, unmodified as normal (not "good" since it's just neutral state)
            UpdateStatus(status, (isModified | !isIdent) ? StatusType.Warning : StatusType.Normal);
        }
        else
        {
            UpdateStatus("No monitor selected", StatusType.Normal);
        }
    }

    private void ToggleInfoPane(object sender, RoutedEventArgs e)
    {
        if (InfoPanel.Visibility == Visibility.Collapsed)
        {
            // Show panel
            InfoColumn.Width = new GridLength(1, GridUnitType.Star);
            InfoPanel.Visibility = Visibility.Visible;
            InfoToggle.Style = (Style)FindResource("TabNavButtonActive");
        }
        else
        {
            // Hide panel
            InfoColumn.Width = new GridLength(0);
            InfoPanel.Visibility = Visibility.Collapsed;
            InfoToggle.Style = (Style)FindResource("TabNavButton");
        }
    }

    private void DisplayBackend_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating || _displayService == null) return;

        _displayService.ActiveBackend = BackendDXGI.IsChecked == true ? DisplayBackend.DXGI : DisplayBackend.GDI;

        // Re-apply current profile through the new backend
        ApplyCurrentProfile();
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating) return;

        if (PresetComboBox.SelectedItem is ComboBoxItem item)
        {
            string presetTag = item.Tag?.ToString();

            if (presetTag == "default")
            {
                // Load default (neutral) preset
                LoadCurrentValues();
                ApplyCurrentProfile();
            }
            else if (!string.IsNullOrEmpty(presetTag))
            {
                // Load user preset by filename
                LoadPreset(presetTag);
            }
        }
    }
    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        string presetName = ShowInputDialog("Save Preset", "Enter a name for this preset:", "My Preset");

        if (string.IsNullOrWhiteSpace(presetName))
            return;

        // Create preset from current settings
        var preset = new UserPreset
        {
            Name = presetName,
            ColorProfile = _currentProfile.Clone(),
            FilterProfile = _currentFilterProfile.Clone(),
            FiltersEnabled = _filtersEnabled
        };

        // Save to file
        SavePreset(preset);

        // Reload presets list
        LoadUserPresets();

        // Select the newly created preset
        foreach (ComboBoxItem item in PresetComboBox.Items)
        {
            if (item.Tag?.ToString() == preset.FileName)
            {
                PresetComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private string ShowInputDialog(string title, string prompt, string defaultValue = "")
    {
        // Temporarily disable topmost and hide parent
        bool wasTopmost = this.Topmost;
        this.Topmost = false;
        this.Visibility = Visibility.Hidden;
        _allowNativeChromeMessages = true;

        // Create dialog
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("Theme.Surface.Dark")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };

        var stack = new StackPanel { Margin = new Thickness(15) };
        stack.Children.Add(new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("Theme.Text.Primary"))
        });

        var textBox = new TextBox { Text = defaultValue, Padding = new Thickness(3), Height = 24 };
        stack.Children.Add(textBox);

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var okButton = new Button { Content = "OK", Width = 75, Height = 36, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Width = 75, Height = 36, IsCancel = true };

        okButton.Click += (s, args) => dialog.DialogResult = true;
        cancelButton.Click += (s, args) => dialog.DialogResult = false;

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        stack.Children.Add(buttonPanel);

        dialog.Content = stack;
        textBox.Focus();
        textBox.SelectAll();

        bool? result = dialog.ShowDialog();

        // Restore state
        _allowNativeChromeMessages = false;
        this.Visibility = Visibility.Visible;
        this.Topmost = wasTopmost;

        // Return result (null if cancelled)
        return result == true ? textBox.Text.Trim() : null;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Debug.Print("Closing!");

        if (_settingsService.Settings.MinimizeOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // If we're actually closing (not minimizing), handle reset
        if (_settingsService.Settings.ResetOnExit)
        {
            if (_filtersEnabled)
            {
                _filterService?.ClearFilter();
            }

            var selectedMonitor = GetSelectedMonitor();
            if (selectedMonitor == null)
                _displayService.ResetAll();
            else
                _displayService.ResetMonitor(selectedMonitor);

            _currentProfile = new ColorProfile();
            InitializeCurveEditors();
            LoadCurrentValues();
        }

        // Actually exit the application
        Application.Current.Shutdown();
    }

    private void BtnRefreshHistogram_Click(object sender, RoutedEventArgs e)
    {
        UpdateHistogram();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void TabSwitch(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        int tabIndex = int.Parse(button.Tag.ToString());
        MainTabs.SelectedIndex = tabIndex;

        // Update active state
        foreach (var child in TabSwitches.Children.OfType<Button>())
        {
            child.Style = (child == button)
                ? (Style)FindResource("TabNavButtonActive")
                : (Style)FindResource("TabNavButton");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void ToggleViz(FrameworkElement control)
    {
        if (control.Tag != null)
        {
            var parts = control.Tag.ToString().Split(',');
            control.Width = double.Parse(parts[0]);
            control.Height = double.Parse(parts[1]);
            control.Tag = null;
        }
        else
        {
            control.Tag = $"{control.Width},{control.Height}";
            control.Width = 0;
            control.Height = 0;
            control.Visibility = Visibility.Collapsed;
        }
    }

    public static void OpenUrl(string url)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
    }

    private void ICCProfileWiki_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://en.wikipedia.org/wiki/ICC_profile");
    }

    private void ICCProfilesURL_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://registry.color.org/profile-library/");
    }

    private void PopOut_Menu(object sender, RoutedEventArgs e)
    {

    }

    public enum StatusType
    {
        Normal,
        Good,
        Warning,
        Error
    }

    public void UpdateStatus(string status, StatusType type = StatusType.Normal)
    {
        StatusBarText.Text = $"STATUS: {status}";

        StatusBarText.Foreground = type switch
        {
            StatusType.Normal => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("Theme.Text.Primary")),
            StatusType.Good => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("Theme.Status.Success")),
            StatusType.Warning => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("Theme.Status.Warning")),
            StatusType.Error => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)FindResource("Theme.Status.Error")),
            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(176, 176, 176))
        };
    }



    private void StartMinimizedCheckbox_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.StartMinimized = StartMinimizedCheckbox.IsChecked.Value;
        _settingsService.Save();
    }

    private void ApplyRealTimeCheckbox_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.RealTimeUpdates = ApplyRealTimeCheckbox.IsChecked.Value;
        _settingsService.Save();
    }

    private void RealTimeHistogramCheckbox_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.RealTimeHistogram = RealTimeHistogramCheckbox.IsChecked.Value;
        _settingsService.Save();
    }

    private void AlwaysOnTopCheckbox_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.AlwaysOnTop = AlwaysOnTopCheckbox.IsChecked.Value;
        _settingsService.Save();

        this.Topmost = AlwaysOnTopCheckbox.IsChecked.Value;
    }

    private void ResetOnExitCheckbox_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.ResetOnExit = ResetOnExitCheckbox.IsChecked.Value;
        _settingsService.Save();
    }

    private void MinimizeOnCloseCheckbox_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.MinimizeOnClose = MinimizeOnCloseCheckbox.IsChecked.Value;
        _settingsService.Save();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _profileDirty = false;
        ApplyProfile();
    }


    #endregion

    #region Preset Management

    private static readonly string PresetsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayControl",
        "Presets"
    );

    private void LoadUserPresets()
    {
        _userPresets.Clear();

        // Ensure presets directory exists
        if (!Directory.Exists(PresetsPath))
            Directory.CreateDirectory(PresetsPath);

        // Load all preset files
        var files = Directory.GetFiles(PresetsPath, "*.json");
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var preset = JsonSerializer.Deserialize<UserPreset>(json);
                if (preset != null)
                {
                    preset.FileName = Path.GetFileName(file);
                    _userPresets.Add(preset);
                }
            }
            catch
            {
                // Skip corrupted presets
            }
        }

        // Rebuild preset ComboBox
        _isUpdating = true;
        PresetComboBox.Items.Clear();

        PresetComboBox.Items.Add(new ComboBoxItem
        {
            Content = "Default",
            Tag = "default"
        });

        foreach (var preset in _userPresets)
        {
            PresetComboBox.Items.Add(new ComboBoxItem
            {
                Content = preset.Name,
                Tag = preset.FileName
            });
        }

        PresetComboBox.SelectedIndex = 0;
        _isUpdating = false;
    }

    private void SavePreset(UserPreset preset)
    {
        try
        {
            if (!Directory.Exists(PresetsPath))
                Directory.CreateDirectory(PresetsPath);

            // Generate safe filename from preset name
            string safeFileName = string.Concat(preset.Name.Split(Path.GetInvalidFileNameChars())) + ".json";
            preset.FileName = safeFileName;

            string filePath = Path.Combine(PresetsPath, safeFileName);

            var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            MessageBox.Show("Failed to save preset.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadPreset(string fileName)
    {
        try
        {
            string filePath = Path.Combine(PresetsPath, fileName);
            if (!File.Exists(filePath))
                return;

            var json = File.ReadAllText(filePath);
            var preset = JsonSerializer.Deserialize<UserPreset>(json);

            if (preset == null)
                return;

            // Load color profile
            ApplyPreset(preset.ColorProfile);

            // Load filter profile
            _isUpdating = true;
            _currentFilterProfile = preset.FilterProfile.Clone();
            _filtersEnabled = preset.FiltersEnabled;

            if (_filtersEnabled && _filterService != null)
            {
                _filterService.ApplyFilter(_currentFilterProfile);
                FilterEnabledCheckbox.IsChecked = true;
            }
            else
            {
                _filterService?.ClearFilter();
                FilterEnabledCheckbox.IsChecked = false;
            }

            // Sync filter UI
            FilterPresetComboBox.SelectedIndex = 0; // Reset to Custom
            UpdateFilterSliders();
            _isUpdating = false;

            ApplyCurrentProfile();
        }
        catch
        {
            MessageBox.Show("Failed to load preset.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    #endregion


}

public class UserPreset
{
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public ColorProfile ColorProfile { get; set; } = new();
    public FilterProfile FilterProfile { get; set; } = new();
    public bool FiltersEnabled { get; set; } = false;
}