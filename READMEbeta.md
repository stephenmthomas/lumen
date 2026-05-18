# μLumen - Display Control

A modern Windows application for controlling screen brightness, contrast, gamma, and color temperature system-wide using native Windows APIs.

## Features

### Advanced Display Manipulation
- **Brightness Control**
- **Contrast Adjustment**
- **Gamma Correction** 
- **Color Temperature**
- **Per-Monitor Control**
- **Preset Profiles**

### System-Wide Hotkeys
Works even when the app doesn't have focus:

- **Ctrl+Alt+Up/Down** - Brightness ±5%
- **Ctrl+Alt+Left/Right** - Contrast ±5%
- **Ctrl+Shift+Up/Down** - Gamma ±5%
- **Ctrl+Shift+Left/Right** - Color Temperature ±100K
- **Ctrl+Alt+R** - Reset to system default

### System Tray Operation
- Runs silently in the system tray
- Double-click tray icon to open main app
- Right-click for quick access to presets and reset

## Architecture

1. **Native Windows App**
   - WPF C# Architecture Developed in Visual Studio 2026
   - Custom Designed UI elements
   - Wraps native Windows API functionality

2. **Built-in Windows APIs**
   - Uses `SetDeviceGammaRamp` from GDI32
   - Adds monitor enumeration for per-display control
   - Includes color temperature algorithms (Tanner Helland's method)
   - Future-ready for ICC profile integration via `mscms.dll`

3. **Modern UI Framework**
   - WPF provides proper DPI scaling
   - System tray integration via NotifyIcon
   - Clean MVVM-ready architecture
   - Dark theme that matches modern Windows


## How It Works

### Gamma Ramp Manipulation
The core technique is GDI op:
1. Get device context for monitor using `GetDC` or `CreateDC`
2. Create a 256-element lookup table (LUT) for each color channel
3. Apply mathematical transformations to the LUT:
   - **Brightness**: Linear scaling `value = input * brightness`
   - **Contrast**: Midpoint scaling `value = ((input - 128) * contrast) + 128`
   - **Gamma**: Power function `value = input^(1/gamma)`
   - **Temperature**: Per-channel RGB multipliers based on Kelvin value
4. Send the transformed LUT to hardware via `SetDeviceGammaRamp`

### Color Transformation Matrix
The core technique uses native Magnification API's internal affine color trannsformations to generate on the fly filters and effects that apply to the entire screen.

The matrix is a 5x5. RGBA plus a 

---

### Filter Tech
The Magnification API filter is a 5×5 color transformation matrix that operates at the DWM (Desktop Window Manager) compositor level.

This 5×5 matrix that transforms RGBA values. Each output channel is a weighted sum of all input channels plus a constant offset:

```
R_out = (R_in × m[0][0]) + (G_in × m[0][1]) + (B_in × m[0][2]) + (A_in × m[0][3]) + m[0][4]
G_out = (R_in × m[1][0]) + (G_in × m[1][1]) + (B_in × m[1][2]) + (A_in × m[1][3]) + m[1][4]
B_out = (R_in × m[2][0]) + (G_in × m[2][1]) + (B_in × m[2][2]) + (A_in × m[2][3]) + m[2][4]
A_out = (R_in × m[3][0]) + (G_in × m[3][1]) + (B_in × m[3][2]) + (A_in × m[3][3]) + m[3][4]
```

The 5th row/column is for the offset (constant addition).

Unlike gamma ramps (which are per-channel 1D LUTs), this matrix enables cross-channel operations:

- Hue rotation — red can become green, green can become blue (impossible with gamma ramps)
- True saturation — move colors toward/away from gray without just scaling channels
- Color swaps — invert specific channels, swap R↔B
- Luminance-preserving operations — adjust color without changing brightness

All using the 5x5 identitiy matrix:

```
1  0  0  0  0
0  1  0  0  0
0  0  1  0  0
0  0  0  1  0
```

---

FILTER

- RGB Offset is additive

---


# Display Control: Color Transformation Fundamentals

A hierarchical breakdown of color manipulation capabilities using 5×5 color transformation matrices, organized by perceptual primitives and visual processing order.

---

## **Level 1: Luminance (Brightness/Lightness)**
*The most fundamental — "can I see it?"*

**What it is:** How bright/dark something appears. Your visual system processes luminance before anything else.

**Matrix operations:**
- **Brightness offset** — add/subtract constant to all channels (implemented)
- **Contrast** — scale around midpoint (implemented)
- **Gamma-like curves** — can't do true gamma in matrix, but can approximate with brightness + contrast combos

**Use cases:**
- Dim screen without reducing color depth
- Boost visibility in dark content (games, movies)
- Compensate for ambient lighting changes

**Missing capabilities:**
- **Black point lift** — raise the floor without crushing highlights
- **White point compression** — lower the ceiling without killing shadows
- These require more sophisticated offset + scaling combos

---

## **Level 2: Chrominance (Color Information)**
*"What color is it?" — comes after luminance in perception*

### **2A: Saturation**
*Distance from gray — how "colorful" vs "neutral"*

**What it is:** Move colors toward/away from their luminance-matched gray.

**Matrix operations:**
- **Desaturation** — blend RGB toward luminance (implemented via `Saturation()`)
- **Selective desaturation** — preserve certain hues while desaturating others (complex matrix)

**Use cases:**
- Reduce eye strain (muted colors)
- "Flat" look for professional work
- Increase pop for entertainment
- Color-blind assist (reduce confusing color contrasts)

**Missing capabilities:**
- **Vibrance** — selective saturation boost (only affects less-saturated colors)
- **Saturation by luminance** — desaturate only shadows or only highlights

### **2B: Hue**
*"Which color?" — red vs green vs blue*

**What it is:** Position on the color wheel.

**Matrix operations:**
- **Hue rotation** — rotate entire color wheel (implemented via `HueRotation()`)
- **Hue shift** — move specific hues (red→orange, green→cyan)

**Use cases:**
- Color grading (teal-and-orange look)
- Correct white balance
- Shift problematic colors (make grass less neon green)

**Missing capabilities:**
- **Selective hue shift** — shift only reds, or only blues (requires HSV→matrix conversion for specific ranges)
- **Hue preservation** — prevent specific colors from shifting during saturation changes

---

## **Level 3: Color Relationships**
*How colors interact with each other*

### **3A: Split Toning**
*Different color casts for shadows vs highlights*

**What it is:** Warm shadows + cool highlights (or vice versa) — classic film look.

**Matrix operations:**
- Tint shadows toward one color
- Tint highlights toward another
- Requires luminance-aware matrix (split at 50% gray)

**Use cases:**
- Cinematic looks (orange shadows, teal highlights)
- Reduce harsh lighting contrast
- Creative color grading

**Status:** Not implemented — this is a significant missing feature.

### **3B: Color Channels (RGB Gains)**
*Independent control of R, G, B intensity*

**What it is:** Scale each color channel independently (implemented).

**Matrix operations:**
- Diagonal scaling: `[R×r, G×g, B×b]`
- Implemented via `ChannelGain()`

**Use cases:**
- Remove color casts
- Simulate color temperature (warm = boost R+G, cool = boost B)
- Fix monitor calibration issues

**Missing capabilities:**
- **Channel offsets** — add constant to R/G/B independently (different from gain)
- **Channel curves** — non-linear per-channel adjustments (not possible in matrix, needs gamma ramp layer)

---

## **Level 4: Special Perceptual Operations**

### **4A: Color Blindness Correction**
*Remap confusing colors to distinguishable ones*

**What it is:** Shift problem hues (red/green for deuteranopia) to blues/yellows. Basic versions implemented.

**Missing capabilities:**
- **Daltonization** — simulate color-blind vision (for testing designs)
- **Contrast enhancement** — boost edges between confusing colors

### **4B: Blue Light Reduction**
*Reduce blue wavelengths for eye comfort / circadian rhythm*

**What it is:** Lower blue channel, optionally compensate with red/green.

**Matrix operations:**
- Simple: `B × 0.5`
- Better: `B × 0.5, R × 1.1, G × 1.05` (warm compensation)

**Status:** Not implemented as dedicated control (can be achieved via channel gains).

### **4C: Grayscale / Desaturation**
*Remove color entirely (implemented as preset)*

**Missing capabilities:**
- **Luminance-weighted grayscale** — perceptually accurate (0.299R + 0.587G + 0.114B) vs simple average
- **Partial grayscale** — only desaturate specific hues (keep skin tones in color, make background B&W)

---

## **Level 5: Cross-Channel & Creative**

### **5A: Color Inversion**
*Flip colors across midpoint*

**Status:** Full invert preset implemented.

**Missing capabilities:**
- **Smart invert** — invert colors but preserve hue relationships (complex)
- **Partial invert** — invert only certain luminance ranges

### **5B: Color Swapping**
*Remap specific colors to others*

**Matrix operations:**
- Swap R↔B channels
- Swap any combination

**Use cases:**
- Artistic effects
- Fix badly-encoded video
- Accessibility (make red=green for testing)

**Status:** Not implemented as direct controls.

---

## **The 5×5 Color Transformation Matrix**

**Structure:**

<h6>

```
R_out = (R_in × m[0][0]) + (G_in × m[0][1]) + (B_in × m[0][2]) + (A_in × m[0][3]) + (1 × m[0][4])
G_out = (R_in × m[1][0]) + (G_in × m[1][1]) + (B_in × m[1][2]) + (A_in × m[1][3]) + (1 × m[1][4])
B_out = (R_in × m[2][0]) + (G_in × m[2][1]) + (B_in × m[2][2]) + (A_in × m[2][3]) + (1 × m[2][4])
A_out = (R_in × m[3][0]) + (G_in × m[3][1]) + (B_in × m[3][2]) + (A_in × m[3][3]) + (1 × m[3][4])
1     = (R_in × m[4][0]) + (G_in × m[4][1]) + (B_in × m[4][2]) + (A_in × m[4][3]) + (1 × m[4][4])
```

</h6>





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





## **Technical Notes**
- Matrix operates at DWM (Desktop Window Manager) compositor level
- Applied via Windows Magnification API
- System-wide effect (one active filter for all displays)
- Conflicts with Windows Accessibility color filters
- Detection via registry check: `HKCU\Software\Microsoft\ColorFiltering`
---





















---

### Advanced Features You Didn't Have

#### Color Temperature
Converts Kelvin temperature (2000K-10000K) to RGB multipliers:
- Warm (2700K) = More red, less blue (like candlelight)
- Neutral (6500K) = Daylight balance
- Cool (9000K) = More blue, less red (like overcast sky)

Algorithm from Tanner Helland's research on blackbody radiation curves.

#### Per-Monitor Support
Enumerates all displays using `EnumDisplayMonitors` and creates separate device contexts for independent control.

#### Combined Profiles
The `ApplyColorProfile` method combines all four parameters into a single gamma ramp calculation, preserving the mathematical relationships between brightness/contrast/gamma/temperature.

## Building

```bash
dotnet restore
dotnet build
```

## Running

```bash
dotnet run
```

Or build as a release executable:
```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## Requirements

- .NET 8.0 or later
- Windows 10/11
- Visual Studio 2022 or Rider (optional, for development)

## Future Enhancements

### ICC Profile Management
The native API stubs are already in place (`AssociateColorProfileWithDevice`, etc.) for proper color management:
```csharp
// Example: Load a custom ICC profile for a monitor
NativeMethods.AssociateColorProfileWithDevice(
    null, // local machine
    "sRGB Color Space Profile.icm",
    "\\\\.\\DISPLAY1"
);
```

### DXGI Integration (Windows 10+)
For more advanced control:
- HDR tone mapping
- 10-bit color support
- Hardware-accelerated gamma curves
- Better multi-monitor isolation

### Auto-Scheduling
- Time-based profile switching (e.g., Night Mode after sunset)
- Ambient light sensor integration
- Per-application profiles

## Notes

- Settings reset to system default when the app exits (this is by design)
- To persist settings across restarts, uncomment the settings save/load code in `SettingsWindow.cs`
- The app requires no special permissions - gamma ramp control is available to all user-mode processes

## Comparison to Your VB App

| Feature | VB WinForms | This WPF App |
|---------|-------------|--------------|
| Window Hack | Transparent overlay | None - runs in tray |
| Focus Required | Yes | No - global hotkeys |
| Multi-Monitor | No | Yes |
| Color Temperature | No | Yes |
| Gamma Curves | Linear only | Power function |
| Presets | No | 4 built-in |
| UI | Keyboard-only | GUI + keyboard |
| Code Structure | Single module | Service layer pattern |

The core technique (gamma ramp manipulation) is identical - we're just wrapping it in a much cleaner architecture with more features.
