# μLumen User Guide

This document covers most features of μLumen from end-user operation through to the technical internals of the color processing pipeline. It is intended for both users who want to get the most out of the application and developers who want to understand or extend the architecture.

---

## Table of Contents

1. [Installation & First Launch](#1-installation--first-launch)
2. [User Interface Overview](#2-user-interface-overview)
3. [Gamma Ramp Controls](#3-gamma-ramp-controls)
4. [Fullscreen Color Matrix Filters](#4-fullscreen-color-matrix-filters)
5. [ICC Profile Management](#5-icc-profile-management)
6. [Presets & Profiles](#6-presets--profiles)
7. [System Tray & Global Hotkeys](#7-system-tray--global-hotkeys)
8. [Application Settings](#8-application-settings)
9. [Visualization: Gamma Ramp Display & Histogram](#9-visualization-gamma-ramp-display--histogram)
10. [Technical Architecture](#10-technical-architecture)
11. [The Gamma Ramp Pipeline (Front to Back)](#11-the-gamma-ramp-pipeline-front-to-back)
12. [The Color Matrix Filter Pipeline](#12-the-color-matrix-filter-pipeline)
13. [Display Backends: GDI vs DXGI](#13-display-backends-gdi-vs-dxgi)
14. [Native API Reference](#14-native-api-reference)
15. [Data Storage & File Formats](#15-data-storage--file-formats)
16. [Troubleshooting](#16-troubleshooting)

---

## 1. Installation & First Launch

μLumen is a standalone .NET 8 WPF application. No installer is needed — run `μLumen.exe` directly.

On first launch the application will:
- Enumerate all connected monitors via `EnumDisplayMonitors`
- Capture the original gamma ramp for each monitor (stored in memory for clean resets)
- Attempt to initialize the DXGI backend (falls back to GDI silently if unavailable)
- Initialize the Magnification API for fullscreen color filters
- Load settings from `%LocalAppData%\μLumen\settings.json` (creates defaults if absent)
- Register global hotkeys
- Create a system tray icon
- Open the settings window (or start minimized, if configured)

The application uses `ShutdownMode="OnExplicitShutdown"` — closing the window does not terminate the process. The tray icon persists, and the window can be re-opened by double-clicking it. Actual exit happens via the tray context menu "Exit" option or by closing the window when "Minimize on Close" is disabled.

---

## 2. User Interface Overview

The settings window uses a fully custom chrome — the native title bar is suppressed via `WM_NCCALCSIZE` interception, and a custom title bar provides drag-to-move, minimize, maximize, and close buttons. Window resizing is handled through `WM_NCHITTEST` with a 6px edge detection zone for all eight resize directions.

DWM attributes applied on startup:
- Immersive dark mode (title bar and system controls)
- Transparent border color (eliminates the white-border-on-focus-loss issue)
- Round window corners
- Mica backdrop (Windows 11; degrades gracefully on Windows 10)

The main layout is organized into a few sections:

- **Gamma Ramp** — All LUT-based color controls (brightness, contrast, gamma, exposure, tonal zones, color temperature, saturation, levels, curves, enhancers)
- **Filters** — Magnification API color matrix controls (presets, cross-channel operations, hue rotation)
- **Settings** — Application behavior configuration
- **Info Panel** — Collapsible side panel with gamma ramp visualization, histogram, and status

A monitor selector dropdown in the toolbar lets you target a specific display or all displays at once.

---

## 3. Gamma Ramp Controls

These controls build a 256-entry RGB lookup table (LUT) that maps input luminance values to output values for each color channel. The Windows GDI function `SetDeviceGammaRamp` applies this LUT to the display hardware's DAC (or its software equivalent in the DWM compositor).

### 3.1 Exposure & Offset

**Exposure** (-3.0 to +3.0 stops): Multiplicative gain in linear light, using the photographic convention `value *= 2^exposure`. One stop doubles (or halves) brightness.

**Offset** (-0.5 to +0.5): Additive shift applied after exposure. Lifts or crushes the entire tonal range uniformly.

**Gamma** (0.1 to 3.0): Power-law correction `value = value^(1/gamma)`. Values below 1.0 darken midtones; values above 1.0 lighten them. This is the classic "gamma correction" used in display calibration.

### 3.2 Tone

**Brightness** (0.0 to 2.0): Final multiplicative scale applied at the very end of the pipeline. Unlike exposure, this operates on the fully processed signal — after gamma correction, curves, and color adjustments.

**Contrast** (0.0 to 2.0): Scales values around the midpoint (0.5). Values below 1.0 reduce contrast (flatten toward gray); values above 1.0 increase contrast.

### 3.3 Tonal Zones

Five-zone adjustment inspired by professional color grading tools. Each zone targets a specific luminance range with Gaussian-weighted smooth falloff:

- **Blacks** (center 0.0, width 0.15): The deepest shadows
- **Shadows** (center 0.15, width 0.25): Low-end detail
- **Midtones** (center 0.5, width 0.3): The bulk of visible content
- **Highlights** (center 0.85, width 0.25): Bright areas
- **Whites** (center 1.0, width 0.15): Specular highlights and peak brightness

The falloff function is `exp(-0.5 * ((value - center) / width)²)`, producing a smooth bell curve with no hard edges between zones.

### 3.4 Color

**Color Temperature** (2000K–10000K): Shifts the white point along the Planckian locus. 6500K is the D65 standard (daylight). Lower values are warmer (more red/yellow); higher values are cooler (more blue). The algorithm uses Tanner Helland's approximation of the CIE chromaticity coordinates, producing R/G/B multipliers that are applied to the channel split.

**Tint** (-1.0 to +1.0): Green/magenta axis shift, orthogonal to color temperature. Positive values add green; negative values add magenta (boost red and blue). Applied as multiplier adjustments to the R/G/B channels alongside color temperature.

**Red/Green/Blue Gain** (0.0 to 2.0 each): Independent per-channel multipliers applied after color temperature. Useful for fine-tuning white balance or creating deliberate color casts.

### 3.5 Saturation & Vibrance

**Saturation** (0.0 to 3.0): Linear interpolation between each channel and the perceptual luminance (`R*0.2126 + G*0.7152 + B*0.0722`, Rec. 709 coefficients). At 0.0 the image is grayscale; at 1.0 it's unchanged; above 1.0, colors are pushed further from gray.

**Vibrance** (-1.0 to 1.0): Selective saturation — muted colors receive more boost than already-saturated colors. The boost factor scales with `(1 - currentSaturation)`, where `currentSaturation` is derived from the HSV model (`(max - min) / max`). This produces more natural-looking results than uniform saturation and protects skin tones.

Note: Because gamma ramps are per-channel LUTs, saturation and vibrance here are approximations — they operate on the luminance-to-channel relationship at each input level, not on actual pixel color. For true cross-channel saturation, use the Filters tab (Magnification API).

### 3.6 Levels

When enabled, levels remaps the input tonal range before any other processing:

- **Input Black** (0–255): Values at or below this become the output black point
- **Input White** (0–255): Values at or above this become the output white point
- **Output Black** (0–255): The minimum output value
- **Output White** (0–255): The maximum output value

The mapping is linear between the input range endpoints. This is the same control found in Photoshop's Levels dialog — useful for expanding or compressing the usable dynamic range.

### 3.7 Tone Curves

μLumen provides four independent tone curve editors: one master (luminance) curve and three per-channel (R, G, B) curves. Each curve is a set of control points interpolated with a monotone cubic Hermite spline (Fritsch-Carlson algorithm).

**Interaction**: Click on the curve canvas to add a control point. Drag existing points to reshape the curve. The curve generates a 256-entry LUT that is applied in the ramp pipeline.

**Monotone cubic spline**: Unlike natural cubic splines, the Fritsch-Carlson method enforces monotonicity — the curve never overshoots or oscillates between control points. This is critical for gamma ramps, where a non-monotonic LUT would create bizarre inversion artifacts.

The master curve is applied to the unified luminance signal before the channel split; per-channel curves are applied independently to R, G, and B after the split.

### 3.8 Dynamic Enhancers

**Dynamic Contrast** (0.0 to 1.0): Applies an S-curve (`3x² - 2x³`, Hermite smoothstep) blended with the original signal. Strongest in the midtones, gentler at the extremes. This enhances perceived contrast without clipping highlights or crushing shadows.

**Blue Light Filter** (0.0 to 1.0): Reduces the blue channel proportionally and slightly reduces green. At full strength, blue output is zero and green is reduced by 20%. Unlike color temperature adjustment, this is a simple channel multiplier — it doesn't attempt to follow the Planckian locus.

**Black Equalizer** (-1.0 to 1.0): Lifts shadow detail by adding brightness weighted by `(1 - value)²` — strongest in the darkest areas, fading to zero effect at white. Useful for gaming (seeing into dark areas) without washing out the rest of the image.

**White Equalizer** (0.0 to 1.0): Compresses highlights above 0.5, reducing brightness of bright areas with a quadratic mask `((value - 0.5) * 2)²`. Recovers detail in overexposed highlights.

---

## 4. Fullscreen Color Matrix Filters

The Filters tab provides a completely separate color processing path that uses the Windows Magnification API (`MagSetFullscreenColorEffect`). This operates at the DWM composition level — it processes pixels after gamma ramps have been applied and before final display output.

The key advantage of matrix filters is that they are true cross-channel operations. A 5×5 color matrix can do things that per-channel LUTs cannot: hue rotation, color-space-aware desaturation, channel mixing, and split toning.

### 4.1 How the Matrix Works

The 5×5 matrix transforms each pixel:

```

R_out = (R_in × m[0][0]) + (G_in × m[0][1]) + (B_in × m[0][2]) + (A_in × m[0][3]) + m[0][4]
G_out = (R_in × m[1][0]) + (G_in × m[1][1]) + (B_in × m[1][2]) + (A_in × m[1][3]) + m[1][4]
B_out = (R_in × m[2][0]) + (G_in × m[2][1]) + (B_in × m[2][2]) + (A_in × m[2][3]) + m[2][4]
A_out = (R_in × m[3][0]) + (G_in × m[3][1]) + (B_in × m[3][2]) + (A_in × m[3][3]) + m[3][4]

```

The 5th column provides additive offsets (brightness, color casts). The identity matrix (1s on the diagonal, 0s elsewhere) produces no change. Multiple operations are composed by matrix multiplication.

**Identity matrix (no transformation):**
```
1  0  0  0  0
0  1  0  0  0
0  0  1  0  0
0  0  0  1  0
0  0  0  0  1
```

**C# Ident. MAT**
```
public static ColorMatrix Identity => new()
{
    M00 = 1, M01 = 0, M02 = 0, M03 = 0, M04 = 0,
    M10 = 0, M11 = 1, M12 = 0, M13 = 0, M14 = 0,
    M20 = 0, M21 = 0, M22 = 1, M23 = 0, M24 = 0,
    M30 = 0, M31 = 0, M32 = 0, M33 = 1, M34 = 0,
    M40 = 0, M41 = 0, M42 = 0, M43 = 0, M44 = 1, 
}
```

Only one fullscreen color effect can be active system-wide at a time — this is a Windows limitation.

### 4.2 Filter Presets

- **None**: Identity matrix (no effect)
- **Grayscale**: Rec. 709 perceptual luminance weights
- **Inverted**: Full color inversion with additive offset
- **Sepia**: Warm-toned desaturation
- **Night Light**: Reduced blue channel, slight warmth, minor brightness reduction
- **High Contrast**: 1.6× contrast scaling
- **Warm Film / Cool Film**: Cinematic color grading
- **Cyberpunk / Noir / Vintage / HDR Effect**: Stylistic presets
- **Protanopia / Deuteranopia / Tritanopia Correction**: Color vision deficiency compensation (shifts information from imperceptible channels into perceivable ones)

**Preset Strength** (0.0 to 1.0): Linear interpolation between the identity matrix and the preset matrix. At 0.5, you get half the effect.

### 4.3 Filter Adjustments

These are composable adjustments that stack on top of the selected preset:

- **Brightness** (-1.0 to +1.0): Additive offset to all channels
- **Contrast** (0.0 to 2.0): Scale around midpoint
- **Saturation** (0.0 to 3.0): True cross-channel saturation using Rec. 709 luminance
- **Hue Rotation** (-180° to +180°): Rotates the color wheel around the luminance axis
- **Inversion Strength** (0.0 to 1.0): Partial color inversion
- **R/G/B Gain** (0.0 to 2.0 each): Per-channel multiplicative gain
- **R/G/B Offset** (-1.0 to +1.0 each): Per-channel additive offset
- **Black Point** (0.0 to 0.3): Lifts blacks (creates a "faded" look)
- **White Point** (0.7 to 1.0): Lowers the white ceiling
- **Vibrance** (-1.0 to 1.0): Selective saturation (matrix approximation)
- **Exposure** (-3.0 to +3.0 stops): Multiplicative gain
- **Temperature** (2000K–10000K): Color temperature shift via channel gain approximation

### 4.4 Matrix Preview

The live 5×5 matrix grid in the UI shows the final composed matrix after all adjustments. This is useful for understanding how individual adjustments interact and for debugging custom color transforms.

---

## 5. ICC Profile Management

The ICC section (in settings) provides management of Windows Color System (WCS) ICC profiles for the selected monitor.

**Important**: You must select a specific monitor from the toolbar dropdown (not "All Monitors") for ICC profile management — ICC profiles are per-device.

### Available Operations

- **Browse**: Lists all `.icc` and `.icm` files in `%SystemRoot%\System32\spool\drivers\color`
- **Apply**: Sets the selected profile as the default for the chosen monitor via `WcsSetDefaultColorProfile` (current-user scope)
- **Refresh**: Re-scans the profile directory
- **Open Profile Folder**: Opens the system color directory in Explorer
- **Open Display Settings**: Launches `ms-settings:display`
- **Open Color Management**: Launches the `colorcpl.exe` control panel applet

### Adding New Profiles

To add a new ICC profile to the system, copy the `.icc` or `.icm` file into `C:\Windows\System32\spool\drivers\color\`, then click Refresh. The profile will appear in the dropdown. You can obtain calibration profiles from your monitor manufacturer, from calibration hardware/software, or from the ICC Profile Registry at `https://registry.color.org/profile-library/`.

---

## 6. Presets & Profiles

### Built-in Presets

Four built-in presets exist in the code but are currently disabled. Details below, but, leverage user presets instead!

| Preset | Brightness | Contrast | Gamma | Color Temp | Saturation | Notes |
|---|---|---|---|---|---|---|
| Default | 1.0 | 1.0 | 1.0 | 6500K | 1.0 | Identity (no change) |
| Night | 0.7 | 1.1 | 1.1 | 3400K | 0.9 | Warm, dim, 50% blue light filter |
| Reading | 0.8 | 1.2 | 1.0 | 4500K | 0.85 | Reduced glare, 30% blue light filter |
| Gaming | 1.2 | 1.3 | 0.9 | 6500K | 1.3 | Boosted visibility, vibrance +0.3, dynamic contrast +0.2, black equalizer +0.3 |

### User Presets

Click "Save Preset" to capture the entire current state — all gamma ramp controls plus all filter settings — into a named JSON file. Presets are stored at `%LocalAppData%\μLumen\Presets\` and appear in the preset dropdown.

Each preset file contains:
- The full `ColorProfile` (all gamma ramp parameters, curve control points, levels)
- The full `FilterProfile` (all matrix filter parameters, preset selection, strength)
- Whether filters were enabled at save time

Loading a preset restores the complete application state.

---

## 7. System Tray & Global Hotkeys

### Tray Icon

The tray icon uses the native Win32 `Shell_NotifyIcon` API — no WinForms dependency. It processes `WM_LBUTTONDBLCLK` (double-click to show settings) and `WM_RBUTTONUP` (right-click for context menu) through a custom `WndProc` hook.

The context menu provides:
- Show Settings
- Presets submenu (Default, Night, Reading, Gaming)
- Reset to System Default
- Exit

### Global Hotkeys

Hotkeys are registered via `RegisterHotKey` with `MOD_NOREPEAT` to prevent rapid-fire when held. They work even when the application is not focused.

| Shortcut | Action |
|---|---|
| `Ctrl+Alt+Up` | Brightness +5 |
| `Ctrl+Alt+Down` | Brightness -5 |
| `Ctrl+Alt+Left` | Contrast -5 |
| `Ctrl+Alt+Right` | Contrast +5 |
| `Ctrl+Shift+Up` | Gamma +5 |
| `Ctrl+Shift+Down` | Gamma -5 |
| `Ctrl+Shift+Left` | Color Temperature -100K |
| `Ctrl+Shift+Right` | Color Temperature +100K |
| `Ctrl+Alt+R` | Reset to defaults |

Hotkey messages are processed through the shared `WndProc` hook — the same hook that handles tray icon messages. The `HotkeyService` maps registered IDs to `Action` callbacks.

---

## 8. Application Settings

Available in the Settings tab. All settings are persisted immediately on change.

- **Start Minimized**: Launch directly to tray without showing the window
- **Minimize on Close**: Close button hides to tray instead of exiting
- **Reset on Exit**: Restore original gamma ramps and clear filters when the application closes
- **Real-Time Updates**: Apply gamma ramp changes as sliders move (disable for manual "Apply" workflow)
- **Real-Time Histogram**: Update the screen histogram every time the gamma ramp changes (adds latency from screen capture)
- **Always on Top**: Keep the settings window above all other windows
- **Display Backend**: Switch between GDI (legacy) and DXGI (modern) gamma ramp paths

---

## 9. Visualization: Gamma Ramp Display & Histogram

### Gamma Ramp Display

The `GammaRampView` control renders the current 256-entry RGB gamma ramp as overlaid curves (red, green, blue channels) using WPF's `OnRender`/`DrawingContext` for direct-mode rendering. The control updates automatically every time `DisplayService` builds a new ramp.

A linear diagonal line (bottom-left to top-right) represents the identity ramp — any deviation from this line represents a color transformation.

### Histogram

The histogram is generated by capturing a downsampled screenshot (640×360 by default) via native GDI `BitBlt` and extracting BGRA pixel data with `GetDIBits`. The capture uses screen-space coordinates and is DPI-aware.

When "Real-Time Histogram" is enabled, the histogram updates after every gamma ramp or filter change, giving live feedback on how adjustments affect the actual screen content.

The "Refresh Histogram" button triggers a manual capture.

---

## 10. Technical Architecture

### Service Initialization (App.OnStartup)

```
App.OnStartup
├── new DisplayService()          // Enumerate monitors, capture original ramps, init DXGI
├── new FilterService()           // Initialize Magnification API
├── new SettingsService()         // Prepare settings persistence
├── new ICCProfileService()       // Prepare ICC enumeration
├── _settingsService.Load()       // Load settings.json
├── new SettingsWindow(...)       // Pass all four services
│   ├── LoadMonitors()            // Populate monitor dropdown
│   ├── InitializeCurveEditors()  // Bind curve objects to UI controls
│   ├── LoadCurrentValues()       // Set all sliders to defaults
│   ├── CheckSystemFilters()      // Warn if Windows color filters are active
│   ├── UpdateHistogram()         // Initial screen capture
│   ├── LoadICCProfiles()         // Enumerate available ICC profiles
│   └── LoadUserPresets()         // Scan presets directory
├── Set ActiveBackend = GDI       // Default to GDI path
├── new HotkeyService(hwnd)       // Register global hotkeys
├── AddHook(WndProc)              // Shared message processing
└── CreateTrayIcon(hwnd)          // Native Shell_NotifyIcon
```

### Message Flow

All Windows messages route through a single `WndProc` hook on the settings window's `HwndSource`:

1. Tray icon messages (`WM_TRAYICON` = 0x8000) are dispatched first
2. Hotkey messages (`WM_HOTKEY` = 0x0312) are dispatched second
3. Non-client area messages (`WM_NCHITTEST`, `WM_NCACTIVATE`, `WM_NCCALCSIZE`) are handled by the SettingsWindow for custom chrome

### Service Wiring

`DisplayService` holds a reference to `SettingsWindow` (set via `SettingsWindow = this` in the constructor). This allows `DisplayService.CreateFullRamp()` to call back into the UI to update the gamma ramp visualization and status bar after every ramp build. This is a deliberate choice over events/callbacks for simplicity in a solo project.

---

## 11. The Gamma Ramp Pipeline (Front to Back)

When any gamma-ramp-related slider changes, the following pipeline executes:

```
Slider ValueChanged event
└── SettingsWindow.ApplyCurrentProfile()
    ├── (if Real-Time disabled) → mark dirty, return
    └── DisplayService.ApplyColorProfileToMonitor(deviceName, profile)
        ├── CreateFullRamp(profile) → builds NativeMethods.GammaRamp
        ├── IsValidGammaRamp(ramp) → monotonicity check
        └── SetDeviceGammaRamp(hdc, ramp) → Win32 API call
```

### CreateFullRamp Pipeline (per-input-value, i = 0..255)

This is the core color math. For each of the 256 input values:

```
1. NORMALIZE           val = i / 255.0
2. LEVELS              if enabled: remap through input/output black/white points
3. ZONE ADJUSTMENTS    apply blacks/shadows/midtones/highlights/whites with Gaussian weights
4. EXPOSURE            val *= 2^exposure  (photographic stops)
5. OFFSET              val += offset
6. CONTRAST            val = (val - 0.5) * contrast + 0.5
7. GAMMA               val = val^(1/gamma)  (after clamping to [0,1])
8. MASTER CURVE        if enabled: val = masterLUT[val*255] / 65535
9. DYNAMIC CONTRAST    if > 0: blend with Hermite smoothstep S-curve
10. CLAMP              val = clamp(val, 0, 1)
11. SPLIT TO R,G,B     r = g = b = val
12. PER-CHANNEL CURVES if enabled: r/g/b = channelLUT[channel*255] / 65535
13. COLOR TEMP + GAINS r *= redMult, g *= greenMult, b *= blueMult
                       (multipliers combine color temperature + tint + per-channel gain)
14. BLUE LIGHT FILTER  if > 0: reduce green slightly, reduce blue proportionally
15. VIBRANCE           selective saturation based on per-value HSV saturation estimate
16. SATURATION         linear interp between channel and Rec.709 luminance
17. BLACK EQUALIZER    lift shadows: val += lift * (1-val)²
18. WHITE EQUALIZER    compress highlights above 0.5
19. BRIGHTNESS         r,g,b *= brightness  (final multiplier)
20. OUTPUT             ramp.Red[i] = clamp(r * 65535, 0, 65535)  (and green, blue)
```

Color temperature multipliers are pre-computed once per ramp build using Tanner Helland's Planckian approximation. Curve LUTs are pre-generated from the spline control points. This keeps the inner loop fast — no per-iteration function calls or allocations.

After building the ramp, `SettingsWindow.UpdateGammaRampControl()` is called to refresh the visual display.

### Validation

Before any ramp is sent to the display driver, `IsValidGammaRamp` checks monotonicity — each entry must be ≥ the previous entry in all three channels. Non-monotonic ramps cause visual artifacts (luminance inversions) and some drivers will reject them.

---

## 12. The Color Matrix Filter Pipeline

The filter pipeline is independent of gamma ramps and uses a different system API:

```
Slider ValueChanged event
└── SettingsWindow.ApplyCurrentFilter()
    ├── FilterProfile.BuildMatrix() → composes 5×5 ColorMatrix
    ├── FilterService.ApplyFilter(profile)
    │   └── MagSetFullscreenColorEffect(matrix) → Magnification API
    └── UpdateMatrixPreview() → display 5×5 grid in UI
```

### Matrix Composition Order

The `BuildMatrix()` method composes individual transformation matrices in a specific order. Matrix multiplication is not commutative — the order matters:

```
1. PRESET BASE         if not None: apply preset matrix (with strength blend)
2. EXPOSURE            multiplicative gain
3. TEMPERATURE         channel gain approximation of Planckian shift
4. LEVELS              black/white point compression
5. INVERSION           partial or full color inversion
6. VIBRANCE            selective saturation (matrix approximation)
7. SATURATION          uniform saturation via luminance mixing
8. HUE ROTATION        rotation around the luminance axis (trigonometric matrix)
9. CHANNEL GAINS       per-channel multiplicative scaling
10. CHANNEL OFFSETS    per-channel additive shift (via 5th matrix column)
11. SPLIT TONING       shadow/highlight color tints (if tints are non-transparent)
12. CONTRAST           scale around midpoint
13. BRIGHTNESS         additive offset (last, to preserve full control)
```

Each step generates a 5×5 matrix, and the steps are composed via matrix multiplication: `result = step_N * (step_N-1 * (... * (step_1 * identity)))`.

---

## 13. Display Backends: GDI vs DXGI

### GDI Backend (Default)

Uses `CreateDC` to get a device context for each monitor, then `SetDeviceGammaRamp` / `GetDeviceGammaRamp` to read and write the 256-entry gamma LUT.

Characteristics:
- 256 control points per channel (8-bit resolution per sample, 16-bit values)
- Universal compatibility (works on all Windows versions)
- Operates through the legacy GDI path
- Some drivers impose ramp restrictions (e.g., values must be within ±128 of identity)

### DXGI Backend

Uses `CreateDXGIFactory` → `EnumAdapters` → `EnumOutputs` → `IDXGIOutput::SetGammaControl`. All COM interface calls are made via manual vtable pointer resolution (`Marshal.GetDelegateForFunctionPointer` + vtable slot offsets) — no SharpDX or other DXGI wrapper libraries.

Characteristics:
- 1025 control points per channel (4× the resolution of GDI)
- Separate `Scale` and `Offset` fields (hardware-accelerated brightness/contrast)
- Works through the graphics driver rather than the legacy GDI path
- Better multi-monitor isolation
- Proper interaction with DWM composition

When DXGI is selected and a GDI-format ramp is produced by `CreateFullRamp`, the `DxgiDisplayBackend.ConvertGdiRampToDxgi` method upsamples 256 → 1025 points using Catmull-Rom cubic interpolation for smooth curves without linear interpolation artifacts.

The backend can be switched at runtime via the radio buttons in the Settings tab. Switching resets the current backend's gamma before activating the new one.

---

## 14. Native API Reference

All P/Invoke declarations are centralized in `NativeMethods.cs`. The application uses the following Windows APIs:

### GDI32 (Gamma Ramp Control)
- `SetDeviceGammaRamp` / `GetDeviceGammaRamp` — Core LUT read/write
- `CreateDC` / `DeleteDC` — Per-monitor device context management

### User32 (Monitor Enumeration & Hotkeys)
- `EnumDisplayMonitors` / `GetMonitorInfo` — Multi-monitor discovery
- `RegisterHotKey` / `UnregisterHotKey` — Global hotkey registration
- `GetDC` / `ReleaseDC` — Screen device context for capture

### DXGI (Modern Gamma Control)
- `CreateDXGIFactory` — DXGI entry point
- COM vtable calls for `IDXGIFactory::EnumAdapters`, `IDXGIAdapter::EnumOutputs`, `IDXGIOutput::GetDesc`, `IDXGIOutput::SetGammaControl`, `IDXGIOutput::GetGammaControlCapabilities`

### Magnification API
- `MagInitialize` / `MagUninitialize` — Lifecycle
- `MagSetFullscreenColorEffect` / `MagGetFullscreenColorEffect` — 5×5 color matrix

### ICM32 / MSCMS (ICC Profiles)
- `EnumColorProfiles` — Enumerate profiles by device, class, or all
- `WcsSetDefaultColorProfile` — Set the default ICC profile for a device
- `AssociateColorProfileWithDevice` / `DisassociateColorProfileFromDevice` — Profile association

### Shell32 (Tray Icon)
- `Shell_NotifyIcon` — System tray icon create/modify/delete

### DWM (Window Appearance)
- `DwmSetWindowAttribute` — Dark mode, border color, corner preference, backdrop type
- `DwmExtendFrameIntoClientArea` — Frame extension for custom chrome
- `DwmIsCompositionEnabled` — Composition check

---

## 15. Data Storage & File Formats

### Settings
**Path**: `%LocalAppData%\μLumen\settings.json`

```json
{
  "StartMinimized": false,
  "MinimizeOnClose": false,
  "ResetOnExit": false,
  "RealTimeUpdates": false,
  "RealTimeHistogram": false,
  "AlwaysOnTop": false
}
```

### User Presets
**Path**: `%LocalAppData%\μLumen\Presets\<PresetName>.json`

Each file is a serialized `UserPreset` containing:
- `Name`: Display name
- `FileName`: Auto-generated safe filename
- `ColorProfile`: Complete gamma ramp profile (all 30+ parameters, including serialized curve control points and levels)
- `FilterProfile`: Complete filter profile (all matrix parameters, preset selection, split toning colors)
- `FiltersEnabled`: Whether the filter system was active when saved

Both files use `System.Text.Json` with `WriteIndented = true`.

---

## 16. Troubleshooting

### "Invalid Gamma Ramp" status
The ramp failed the monotonicity check — some combination of extreme settings produced a non-monotonic LUT. Reduce the intensity of active adjustments (particularly levels, curves, or extreme contrast/gamma combinations).

### Changes don't seem to apply
Check that "Real-Time Updates" is enabled in Settings. If disabled, you need to click the Apply button manually.

### White border flashes when the window loses focus
This should be resolved by the DWM border color override (transparent) and `WM_NCACTIVATE` interception. If it persists, ensure you're running Windows 10 1809+ or Windows 11.

### Tray icon doesn't appear
The tray icon requires `app.ico` to be present alongside the executable. Ensure the icon file is included as a `Resource` in the build output.

### Filters have no effect
Only one fullscreen Magnification API color effect can be active at a time system-wide. Check if Windows Accessibility color filters are active (Settings → Accessibility → Color Filters) — μLumen will show a warning if it detects this. Also verify that the "Enable Filters" checkbox is checked.

### DXGI backend fails silently
Some GPU drivers don't support `IDXGIOutput::SetGammaControl` in windowed mode — it officially requires a fullscreen exclusive Direct3D application. The GDI backend is the reliable fallback.

### ICC profile application fails
`WcsSetDefaultColorProfile` operates at the current-user scope and may require the profile to be physically present in `%SystemRoot%\System32\spool\drivers\color`. Copy the file there first, then refresh and apply.

### Gamma ramp resets on its own
Other applications (f.lux, Windows Night Light, NVIDIA color settings) can overwrite the gamma ramp. μLumen detects this and reports "Modified Ramp" vs "App Modified" in the status bar. Disable competing color management tools for best results.

---

*μLumen — by S.T.*


--- 

#PENDING UPDATES

Gemini says:

Since you have already built out a comprehensive parametric matrix system (which is fantastic, by the way!), you have likely bumped into the fundamental limitation of a 5x5 color matrix: it is purely linear.
Because a matrix cannot perform true non-linear math (like exponents or power curves required for true Gamma, Shadows, or Highlights), we have to cheat. We can use mathematical approximations that mimic these curves beautifully by combining the linear gains ($M_{00}, M_{11}, M_{22}$) with the translation offsets ($M_{40}, M_{41}, M_{42}$) relative to specific thresholds.
Here is how you can implement approximations for Gamma, Shadows, and Highlights into your mutable matrix system.
------------------------------
## 1. Matrix Approximation of Gamma (Power Curve)
A true gamma curve uses an exponent ($Output = Input^{1/\gamma}$). In a linear matrix, we can approximate a gamma curve using a quadratic Taylor expansion mapped onto the slope (gain) and lift (offset).

* Lowering Gamma compresses midtones down while locking black and white points.
* Raising Gamma pushes midtones up.

public static ColorMatrix ApproximateGamma(float gamma)
{
    // Clamp gamma to a reasonable range (e.g., 0.5 to 2.5)
    gamma = Math.Clamp(gamma, 0.5f, 2.5f);
    
    // Linear approximation factors for midtone shifting
    float gain = (float)Math.Pow(1.0f, 1.0f / gamma);
    
    // We adjust the offset to bow the midtones without shifting the absolute black point
    // An empirical curve mapping for linear approximation:
    float offset = (1.0f - gamma) * 0.15f; 

    return new ColorMatrix
    {
        M00 = gain + offset, M01 = 0,             M02 = 0,             M03 = 0, M04 = 0,
        M10 = 0,             M11 = gain + offset, M12 = 0,             M13 = 0, M14 = 0,
        M20 = 0,             M21 = 0,             M22 = gain + offset, M23 = 0, M24 = 0,
        M30 = 0,             M31 = 0,             M32 = 0,             M33 = 1, M34 = 0,
        M40 = -offset * 0.5f,M41 = -offset * 0.5f,M42 = -offset * 0.5f,M43 = 0, M44 = 1,
    };
}

------------------------------
## 2. Matrix Approximation of Shadows & Highlights
In photo editing software, Shadows and Highlights selectively alter only the very dark or very bright regions of the image.
To achieve this in a linear matrix, we use a Pivot and Scale technique. We treat the offset row ($M_{4x}$) as our directional anchor, and adjust the channel multipliers to collapse or expand colors around a virtual threshold.
## Shadows (Compressing or Recovering Darks)
To approximate a shadow control, we pivot around a low intensity point (roughly $0.2$ on a $0.0$ to $1.0$ scale).

* Positive values recover shadow details (lift blacks into dark grays).
* Negative values crush shadows (make dark grays pure black).

public static ColorMatrix Shadows(float shadows)
{
    // shadows parameter: -1.0f (deeply crushed) to +1.0f (highly recovered)
    // Map to a usable matrix multiplier scale
    float scale = 1.0f + (shadows * 0.25f);
    
    // The offset acts as the pivot point anchor
    float offset = -0.2f * (scale - 1.0f);

    return new ColorMatrix
    {
        M00 = scale, M01 = 0,     M02 = 0,     M03 = 0, M04 = 0,
        M10 = 0,     M11 = scale, M12 = 0,     M13 = 0, M14 = 0,
        M20 = 0,     M21 = 0,     M22 = scale, M23 = 0, M24 = 0,
        M30 = 0,     M31 = 0,     M32 = 0,     M33 = 1, M34 = 0,
        M40 = offset,M41 = offset,M42 = offset,M43 = 0, M44 = 1,
    };
}

## Highlights (Compressing or Recovering Brights)
For highlights, we pivot around the maximum white point ($1.0$).

* Negative values compress blown-out highlights down into soft grays.
* Positive values overexpose the highlights.

public static ColorMatrix Highlights(float highlights)
{
    // highlights parameter: -1.0f (compressed/recovered) to +1.0f (blown out)
    float scale = 1.0f + (highlights * 0.25f);
    
    // Pivot anchored at 1.0 (White Point)
    float offset = 1.0f * (1.0f - scale);

    return new ColorMatrix
    {
        M00 = scale, M01 = 0,     M02 = 0,     M03 = 0, M04 = 0,
        M10 = 0,     M11 = scale, M12 = 0,     M13 = 0, M14 = 0,
        M20 = 0,     M21 = 0,     M22 = scale, M23 = 0, M24 = 0,
        M30 = 0,     M31 = 0,     M32 = 0,     M33 = 1, M34 = 0,
        M40 = offset,M41 = offset,M42 = offset,M43 = 0, M44 = 1,
    };
}

------------------------------
## 3. Finer Control Channels Worth Exploring
Since you already have advanced channels like Hue Rotation and Tone Balance, here are two more finer-control paths you can plug directly into your FilterProfile:
## A. Clarity / Local Contrast Simulation
While true clarity requires a spatial blur matrix, you can simulate a global "Clarity/Pop" effect by isolating the midtones. This matrix pushes midtones away from gray toward absolute black or white without clipping the extreme edges:

public static ColorMatrix Clarity(float clarity)
{
    // clarity: -1.0f to +1.0f
    float slope = 1.0f + clarity * 0.15f;
    float offset = -0.5f * (slope - 1.0f);

    return new ColorMatrix
    {
        M00 = slope, M01 = 0,     M02 = 0,     M03 = 0, M04 = 0,
        M10 = 0,     M11 = slope, M12 = 0,     M13 = 0, M14 = 0,
        M20 = 0,     M21 = 0,     M22 = slope, M23 = 0, M24 = 0,
        M30 = 0,     M31 = 0,     M32 = 0,     M33 = 1, M34 = 0,
        M40 = offset,M41 = offset,M42 = offset,M43 = 0, M44 = 1,
    };
}

## B. Dynamic "Eased" Multiplication (To avoid hard clipping)
When combining all these matrices via matrix multiplication ($A \times B \times C$), your scale values can stack exponentially and aggressively clip to pure white or pure black.
To gain finer control, you can apply a Sigmoid-like easing function to your slider inputs before feeding them into the matrix constructor. For instance, instead of passing raw Brightness linearly, pass it through an easing function so the slider has ultra-fine control near the middle ($0$) and gets progressively more aggressive at the edges:

// Smooth step easing to give fine control in the center of your UI slidersprivate float EaseSliderInput(float value)
{
    // Assumes value is normalized between -1.0 and 1.0
    return MathF.Sign(value) * MathF.Pow(MathF.Abs(value), 1.5f);
}

How are you currently multiplying your matrices together to build the final composition? If you want, I can show you how to structure an optimized combining pipeline that prevents color math overflow when stacking all these parameters.

---

but, alas, i say:

The gamma approximation is weak. Math.Pow(1.0f, 1.0f / gamma) is always 1.0 regardless of gamma — the gain term does nothing. The whole effect comes entirely from the offset, which makes it basically a brightness shift with a misleading name. The Shadows and Clarity matrices are more coherent.

---

and gemini says:

You are entirely right, and I appreciate you catching that. That was an embarrassing mathematical oversight on my part. Math.Pow(1.0f, ...) will always resolve to 1.0, meaning that entire formula collapsed into a flat linear offset—which is exactly why it just felt like a cheap, misleading brightness slider.
Because a 5x5 matrix cannot perform exponents, we cannot truly isolate the midtones while pinning both absolute black ($0.0$) and absolute white ($1.0$) using a single linear transform. However, if we want a coherent Gamma approximation, we have to use a parabolic or piece-wise linear approximation that shifts the midpoint ($0.5$) while forcing the bounds to converge.
Here is a corrected, statistically stable approach to approximating a gamma curve using slope/intercept manipulation that actually targets the midtones instead of turning into a flat brightness shift.
## The Corrected Gamma Matrix Approximation
To make a linear matrix mimic a gamma power curve, we must scale the channels (gain) and shift them (offset) in inverse directions.

* To simulate a lower Gamma ($\gamma < 1.0$, brighter midtones): We increase the offset (lift the darks) but decrease the gain (pull down the whites) so the white point stays anchored at $1.0$.
* To simulate a higher Gamma ($\gamma > 1.0$, darker midtones): We drop the offset (crush the darks) but increase the gain to pull the whites back up to $1.0$.

public static ColorMatrix ApproximateGamma(float gamma)
{
    // Clamp to safe functional ranges (e.g., 0.5 to 2.0. 1.0 is neutral)
    gamma = Math.Clamp(gamma, 0.5f, 2.0f);

    // Calculate how much the midtone needs to bow
    // If gamma = 0.5 (bright midtones), offset is positive, gain is less than 1
    // If gamma = 2.0 (dark midtones), offset is negative, gain is greater than 1
    float offset = (1.0f - gamma) * 0.18f; 
    float gain = 1.0f - offset; 

    return new ColorMatrix
    {
        M00 = gain, M01 = 0,    M02 = 0,    M03 = 0, M04 = 0,
        M10 = 0,    M11 = gain, M12 = 0,    M13 = 0, M14 = 0,
        M20 = 0,    M21 = 0,    M22 = gain, M23 = 0, M24 = 0,
        M30 = 0,    M31 = 0,    M32 = 0,    M33 = 1, M34 = 0,
        M40 = offset, M41 = offset, M42 = offset, M43 = 0, M44 = 1,
    };
}

## Why This One Works Coherently
If you test this matrix mathematically against extreme pixels, you will see it preserves the endpoints much better than a simple brightness shift:

* Black Point ($0,0,0$): Becomes ($offset$). It does introduce a small amount of black-level lift or crush, which is the inevitable tax of a linear matrix trying to mimic a curve.
* White Point ($1,1,1$): Becomes ($gain + offset$). Because $gain = 1.0 - offset$, the math resolves to: $(1.0 - offset) + offset = 1.0$. The white point remains perfectly locked.

## Taking It Further: Splitting the Slopes
Since you noted that the Shadows and Clarity matrices felt structurally sound, you can apply that exact same Pivot and Scale logic to build an alternative "Midtone Gamma" slider.
Instead of targeting the white point, you can anchor the pivot directly at the center ($0.5$). This creates a Contrast Stretch / Midtone Gamma hybrid that expands or compresses exclusively around the center of your histogram:

public static ColorMatrix MidtoneGamma(float adjustment)
{
    // adjustment: -0.5f (darken midtones) to +0.5f (brighten midtones)
    float gain = 1.0f + adjustment;
    float offset = -0.5f * adjustment; // Pivot anchored exactly at 0.5 gray

    return new ColorMatrix
    {
        M00 = gain, M01 = 0,    M02 = 0,    M03 = 0, M04 = 0,
        M10 = 0,    M11 = gain, M12 = 0,    M13 = 0, M14 = 0,
        M20 = 0,    M21 = 0,    M22 = gain, M23 = 0, M24 = 0,
        M30 = 0,    M31 = 0,    M32 = 0,    M33 = 1, M34 = 0,
        M40 = offset, M41 = offset, M42 = offset, M43 = 0, M44 = 1,
    };
}

How does this corrected anchored curve hold up against your current filter profile pipeline? If you want to dive deeper into other channels, we can look into setting up a per-channel pivot matrix to give you finer control over Temperature / Tint balances.

---

but... The corrected gamma is much more honest.The corrected gamma is much more honest. The white-point lock (gain + offset = 1.0 by construction) is the key insight, and it's now actually doing something distinct from brightness.
One thing worth noting: MidtoneGamma with pivot at 0.5 is indeed your existing Contrast method, just reparameterized. They're mathematically the same transform. So you'd be adding ApproximateGamma (white-locked, pivoting implicitly near the top), Shadows, and Highlights as genuinely new controls.