# Zen Garden Branding Asset Specifications

## Image Assets

### GTK Installer Banner (`zen-banner-800x75.png`)

**Specification** (Debian requirement):
- **Dimensions**: 800×75 pixels (exact)
- **Color Depth**: 16-bit color (65,536 colors)
- **Format**: PNG
- **Purpose**: Header banner displayed throughout graphical installer (~10-15 minutes visibility)

**Design Guidelines**:
- **Layout**: Horizontal banner, brand identity left, tagline right
- **Colors**: Cyan accent (#17A2B8), neutral grays (#F5F5F5 background)
- **Typography**: Clean sans-serif (e.g., Open Sans, Inter)
- **Imagery**: 🪨 stone icon, 🌿 garden motif, minimal decoration
- **Mood**: Calm, professional, minimalist

**Example Layout**:
```
┌────────────────────────────────────────────────────────────────────────┐
│                                                                        │
│  🪨 Zen Garden Stone Installer          Calm Infrastructure          │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

---

### ISOLINUX Splash Screen (`zen-splash-640x300.png`)

**Specification** (Debian requirement):
- **Dimensions**: 640×300 pixels (exact)
- **Color Depth**: 4-bit color (16 colors)
- **Format**: PNG (will be converted to ASCII text)
- **Purpose**: Boot splash screen for BIOS mode

**Design Guidelines**:
- **Note**: This image is converted to ASCII art by the preparation script
- Use high contrast, simple shapes (conversion may lose detail)
- Text should be large and readable
- Avoid gradients or complex imagery

**Current Implementation**:
The preparation script generates ASCII art automatically. You can replace
`zen-splash-640x300.png` with custom artwork, or edit the ASCII output
directly in `prepared/isolinux/zen-splash.txt` after preparation.

---

### Background Image (`zen-bg.png`) — OPTIONAL

**Specification**:
- **Color**: `#EFF3EC` solid color (or 256×256 px tileable texture)
- **Format**: PNG
- **Purpose**: GTK installer background

**Current Design Decision**: **Solid color #EFF3EC** (no texture)
- Keeps UI clean and readable
- Texture can be added later if desired (256×256 tileable pattern)

**If using texture (alternative)**:
- **Very subtle**: 5-10% opacity, low contrast
- **Pattern**: Sand, gravel, stone texture
- **Tileable**: Must repeat seamlessly
- **Base color**: Start with `#EFF3EC` and add subtle detail

**Note**: This is optional. GTK theme already specifies solid colors in `gtkrc`.

---

## Creating Assets

### Option 1: Design Tools (Recommended)

**Vector Design** (scalable):
- Use Figma, Inkscape, or Adobe Illustrator for initial design
- Export to PNG at exact dimensions

**Raster Design**:
- Use GIMP, Photoshop, or Photopea
- Create new image with exact dimensions
- Reduce color depth if needed (Image → Mode → Indexed Color)

### Option 2: Template Placeholders

If you don't have custom assets yet, create simple placeholder images:

```powershell
# Placeholder banner (PowerShell + System.Drawing)
Add-Type -AssemblyName System.Drawing
$banner = New-Object System.Drawing.Bitmap(800, 75)
$graphics = [System.Drawing.Graphics]::FromImage($banner)
$graphics.Clear([System.Drawing.Color]::FromArgb(245, 245, 245))
$font = New-Object System.Drawing.Font("Arial", 20)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Black)
$graphics.DrawString("🪨 Zen Garden Stone Installer", $font, $brush, 20, 20)
$banner.Save("zen-banner-800x75.png", [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$banner.Dispose()
```

---

## Validation

After creating assets, validate specifications:

```powershell
.\Prepare-BrandingArtifacts.ps1 -ValidateOnly
```

This checks:
- ✅ File exists
- ✅ Dimensions match requirements
- ✅ Color depth (if applicable)

---

## References

- [Debian Artwork Requirements](https://wiki.debian.org/DebianDesktop/Artwork/Requirements)
- [INSTALLER-CUSTOMIZATION-ANALYSIS.md](../../docs/INSTALLER-CUSTOMIZATION-ANALYSIS.md)
- Zen Garden brand guidelines: (TBD)
