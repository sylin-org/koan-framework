# Zen Garden Installer Branding

This directory contains branding assets and preparation scripts for the Stone USB installer.

## Directory Structure

```
branding/
├── Prepare-BrandingArtifacts.ps1   # Run this to prepare artifacts for NewStone.ps1
├── source/                          # Edit these assets (version-controlled)
│   ├── zen-banner-800x75.png       # GTK installer banner (800×75, 16-bit color)
│   ├── zen-splash-640x300.png      # ISOLINUX splash screen (640×300, 16 colors)
│   ├── zen-texture.png             # Optional background texture (any size, tileable)
│   ├── grub-theme.txt              # GRUB menu configuration
│   ├── isolinux-menu.txt           # ISOLINUX menu configuration
│   └── gtk-theme-gtkrc.txt         # GTK2 theme configuration
└── prepared/                        # Build output (git-ignored, regenerate as needed)
    ├── manifest.json               # Metadata (preparation timestamp, source hashes)
    ├── isolinux/
    ├── boot/grub/
    ├── gtk-initrd/
    └── stone-root/
```

## Quick Start

### 1. Prepare Branding Assets (One-Time)

```powershell
# Full preparation (includes GTK initrd repacking - requires WSL)
.\Prepare-BrandingArtifacts.ps1

# Quick preparation (skip GTK initrd, only boot menus + first-boot)
.\Prepare-BrandingArtifacts.ps1 -SkipInitrd

# Validate source assets without preparing
.\Prepare-BrandingArtifacts.ps1 -ValidateOnly
```

### 2. Create Branded USB

```powershell
# NewStone.ps1 automatically detects and applies prepared branding
cd ..\
.\NewStone.ps1

# If branding/prepared/ exists, NewStone.ps1 will apply:
# - ISOLINUX splash screen (BIOS boot)
# - First-boot MOTD and SSH banners
# - (Future) GRUB theme colors
# - (Future) GTK installer theme and banner
```

**Note**: Branding is optional. If `prepared/` doesn't exist, NewStone.ps1 creates a standard (unbranded) Stone USB.

## Integration with NewStone.ps1

The branding integration is automatic and non-intrusive:

1. **Preparation Phase** (manual, one-time):
   - Run `Prepare-BrandingArtifacts.ps1` to generate `prepared/` directory
   - Committed source assets → build-time artifacts

2. **USB Creation Phase** (automatic):
   - NewStone.ps1 detects `prepared/` directory
   - Calls `Copy-BrandingAssets` function after stone files are written
   - Copies splash screen, first-boot assets, and theme configs
   - If `prepared/` is missing: skips branding (no error)

3. **Boot Experience**:
   - BIOS boot: Shows zen-splash.txt before menu (if present)
   - UEFI boot: Standard GRUB menu (colors configurable in future)
   - Installer: GTK theme applied (requires initrd repacking - future)
   - First boot: Branded MOTD and SSH banner displayed

## What Gets Branded

### ✅ Currently Implemented

- **ISOLINUX Splash** (BIOS boot): ASCII art splash screen before menu
- **First-Boot Assets**: MOTD template, SSH banner, stone identity message

### 🚧 Future Enhancements

- **GRUB Colors**: Apply color scheme from grub-theme.txt
- **GTK Installer**: Banner image + gtkrc theme (requires initrd repacking with WSL)

## Branding Layers
.\Prepare-BrandingArtifacts.ps1

# Quick preparation (skip initrd, for testing boot menus only)
.\Prepare-BrandingArtifacts.ps1 -SkipInitrd
```

### 2. Create Branded USB

```powershell
cd ..
.\NewStone.ps1  # Now uses prepared branding artifacts automatically
```

## Asset Specifications

| Asset | Specification | Purpose |
|-------|---------------|---------|
| **zen-banner-800x75.png** | 800×75 px, 16-bit color (PNG) | GTK installer header banner |
| **zen-splash-640x300.png** | 640×300 px, 16 colors (PNG) | ISOLINUX boot splash screen |
| **zen-texture.png** | Any size, tileable (PNG) | Optional GTK background texture |

**Reference**: [Debian Artwork Requirements](https://wiki.debian.org/DebianDesktop/Artwork/Requirements)

## Customization Workflow

### Option A: Simple Text-Only Customization

Edit text configurations only (no image changes):

```powershell
# Edit menu text
notepad source\grub-theme.txt
notepad source\isolinux-menu.txt

# Re-prepare artifacts
.\Prepare-BrandingArtifacts.ps1 -SkipInitrd

# Test
..\NewStone.ps1 -UsbDrive "E:"
```

### Option B: Full Branding Customization

Replace images and themes:

1. Edit `source/*.png` files (ensure correct dimensions)
2. Edit `source/*.txt` theme files
3. Run `.\Prepare-BrandingArtifacts.ps1`
4. Test with `.\NewStone.ps1`

## Dependencies

### Windows-Only (No Dependencies)
- ✅ GRUB/ISOLINUX menu customization
- ✅ MOTD/first-boot asset preparation

### WSL Required (for GTK Initrd Repacking)
- ⚠️ GTK banner injection (requires `cpio`, `gzip`)
- Install: `wsl --install` (one-time setup)

**Skip WSL**: Use `-SkipInitrd` flag for quick boot menu testing

## Troubleshooting

### "Prepared artifacts are stale"

```powershell
# Freshness warning appears when source/ assets are newer than prepared/
# Solution: Re-run preparation script
.\Prepare-BrandingArtifacts.ps1
```

### "WSL not found" (when preparing GTK assets)

```powershell
# Option 1: Install WSL (one-time)
wsl --install

# Option 2: Skip GTK customization (boot menus only)
.\Prepare-BrandingArtifacts.ps1 -SkipInitrd
```

### "Image dimensions incorrect"

```powershell
# Validate image specs
.\Prepare-BrandingArtifacts.ps1 -ValidateOnly
```

## CI/CD Integration

```yaml
# GitHub Actions example
- name: Prepare Branding Artifacts
  run: |
    cd installer/branding
    pwsh -File Prepare-BrandingArtifacts.ps1 -Force
    
- name: Build USB Image
  run: |
    cd installer
    pwsh -File NewStone.ps1 -UsbDrive "TestDrive" -Force
```

## Design Guidelines

**Zen Garden Visual Identity**:
- **Colors**: Cyan accent (#17A2B8), neutral grays
- **Typography**: Clean sans-serif fonts
- **Mood**: Calm, professional, minimalist
- **Imagery**: Stone/garden themes, subtle textures

**Reference**: [INSTALLER-CUSTOMIZATION-ANALYSIS.md](../docs/INSTALLER-CUSTOMIZATION-ANALYSIS.md)
