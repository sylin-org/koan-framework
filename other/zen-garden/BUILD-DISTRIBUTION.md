# Zen Garden Distribution Build Plan

**Purpose:** Build production-ready binaries for Garden-Moss (Linux) and Rake (Linux + Windows)  
**Date:** January 15, 2026  
**Target:** USB deployment via NewStone.ps1

---

## Build Artifacts

### Required Binaries

1. **garden-moss** (Linux x86_64)
   - Target: `x86_64-unknown-linux-gnu`
   - Location: `/usr/local/bin/garden-moss` on Stone
   - Size: ~15MB (optimized release build)

2. **garden-rake** (Linux x86_64)
   - Target: `x86_64-unknown-linux-gnu`
   - Location: `/usr/local/bin/garden-rake` on Stone
   - Size: ~8MB (optimized release build)

3. **garden-rake.exe** (Windows x86_64)
   - Target: `x86_64-pc-windows-msvc`
   - Location: Developer's PATH
   - Size: ~10MB

### Configuration Files

- `garden-moss.service` - systemd unit file
- `garden-moss.toml` - Garden-Moss configuration
- `bash_completion.d/garden-rake` - Bash completion (optional)

---

## Build Commands

### Local Development Build (Fast)

```powershell
# From zen-garden root
cd other/zen-garden

# Build both binaries (debug)
cargo build --bin garden-moss
cargo build --bin garden-rake

# Outputs:
# target/debug/garden-moss
# target/debug/garden-rake.exe (on Windows)
```

### Production Release Build (Optimized)

```powershell
# Linux binaries (requires Linux or WSL)
cargo build --release --bin garden-moss
cargo build --release --bin garden-rake

# Windows binary (cross-compile from Linux)
cargo build --release --bin garden-rake --target x86_64-pc-windows-msvc

# Outputs:
# target/release/garden-moss
# target/release/garden-rake
# target/x86_64-pc-windows-msvc/release/garden-rake.exe
```

### Cross-Platform Build Script

**build-dist.ps1** (Windows PowerShell):
```powershell
<#
.SYNOPSIS
    Build distribution binaries for Zen Garden
#>
param(
    [switch]$Release,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

Write-Host "Building Zen Garden Distribution..." -ForegroundColor Cyan

# Navigate to workspace root
Push-Location $PSScriptRoot/..

try {
    # Build flags
    $buildType = if ($Release) { "--release" } else { "" }
    $outDir = if ($Release) { "release" } else { "debug" }
    
    # Run tests first
    if (-not $SkipTests) {
        Write-Host "`nRunning tests..." -ForegroundColor Yellow
        cargo test --workspace
        if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
    }
    
    # Build Linux binaries (requires WSL or Linux)
    if ($IsWindows) {
        Write-Host "`nBuilding Linux binaries (requires WSL)..." -ForegroundColor Yellow
        wsl bash -c "cd `$(wslpath '$PWD') && cargo build $buildType --bin garden-moss"
        wsl bash -c "cd `$(wslpath '$PWD') && cargo build $buildType --bin garden-rake"
        
        # Copy to bin/ directory
        New-Item -ItemType Directory -Force -Path "bin" | Out-Null
        Copy-Item "target/$outDir/garden-moss" "bin/garden-moss" -Force
        Copy-Item "target/$outDir/garden-rake" "bin/garden-rake" -Force
    } else {
        # Native Linux build
        Write-Host "`nBuilding Linux binaries..." -ForegroundColor Yellow
        cargo build $buildType --bin garden-moss
        cargo build $buildType --bin garden-rake
    }
    
    # Build Windows binary
    Write-Host "`nBuilding Windows binary..." -ForegroundColor Yellow
    cargo build $buildType --bin garden-rake --target x86_64-pc-windows-msvc
    
    # Copy Windows binary to bin/
    New-Item -ItemType Directory -Force -Path "bin" | Out-Null
    Copy-Item "target/x86_64-pc-windows-msvc/$outDir/garden-rake.exe" "bin/garden-rake.exe" -Force
    
    Write-Host "`n✓ Build complete!" -ForegroundColor Green
    Write-Host "`nArtifacts:" -ForegroundColor Cyan
    Get-ChildItem bin/ | ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 2)
        Write-Host "  $($_.Name) - ${size}MB"
    }
    
} finally {
    Pop-Location
}
```

**build-dist.sh** (Linux Bash):
```bash
#!/bin/bash
# Build distribution binaries for Zen Garden

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.."

# Parse arguments
RELEASE=""
SKIP_TESTS=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --release) RELEASE="--release"; shift ;;
        --skip-tests) SKIP_TESTS="1"; shift ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

OUT_DIR="${RELEASE:+release}"
OUT_DIR="${OUT_DIR:-debug}"

echo "Building Zen Garden Distribution..."

# Run tests
if [ -z "$SKIP_TESTS" ]; then
    echo -e "\nRunning tests..."
    cargo test --workspace
fi

# Build Linux binaries
echo -e "\nBuilding Linux binaries..."
cargo build $RELEASE --bin garden-moss
cargo build $RELEASE --bin garden-rake

# Build Windows binary (requires mingw-w64)
echo -e "\nBuilding Windows binary..."
cargo build $RELEASE --bin garden-rake --target x86_64-pc-windows-gnu

# Copy to bin/ directory
mkdir -p bin
cp "target/$OUT_DIR/garden-moss" bin/
cp "target/$OUT_DIR/garden-rake" bin/
cp "target/x86_64-pc-windows-gnu/$OUT_DIR/garden-rake.exe" bin/

echo -e "\n✓ Build complete!"
echo -e "\nArtifacts:"
ls -lh bin/
```

---

## Size Optimization

### Release Build Flags (Cargo.toml)

```toml
[profile.release]
opt-level = "z"          # Optimize for size
lto = true               # Enable link-time optimization
codegen-units = 1        # Better optimization (slower build)
strip = true             # Strip symbols
panic = "abort"          # Smaller binary
```

### Expected Sizes

| Binary | Debug | Release | Release + Strip |
|--------|-------|---------|-----------------|
| garden-moss | 45MB | 18MB | 15MB |
| garden-rake (Linux) | 35MB | 10MB | 8MB |
| garden-rake.exe (Windows) | 40MB | 12MB | 10MB |

---

## CI/CD Pipeline (GitHub Actions)

```yaml
name: Build Distribution

on:
  push:
    tags:
      - 'v*'
  workflow_dispatch:

jobs:
  build-linux:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: dtolnay/rust-toolchain@stable
      
      - name: Build Linux binaries
        run: |
          cargo build --release --bin garden-moss
          cargo build --release --bin garden-rake
      
      - name: Upload artifacts
        uses: actions/upload-artifact@v3
        with:
          name: linux-binaries
          path: |
            target/release/garden-moss
            target/release/garden-rake

  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - uses: dtolnay/rust-toolchain@stable
      
      - name: Build Windows binary
        run: cargo build --release --bin garden-rake
      
      - name: Upload artifacts
        uses: actions/upload-artifact@v3
        with:
          name: windows-binaries
          path: target/release/garden-rake.exe

  create-release:
    needs: [build-linux, build-windows]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v3
      
      - name: Create Release
        uses: softprops/action-gh-release@v1
        with:
          files: |
            linux-binaries/*
            windows-binaries/*
          draft: false
          prerelease: false
```

---

## Installation Verification

### On Stone (Linux)

```bash
# Check binaries installed
which garden-moss
which garden-rake

# Check versions
garden-moss --version
garden-rake --version

# Check systemd service
systemctl status garden-moss

# Test commands
garden-rake list
garden-rake status
```

### On Developer Machine (Windows)

```powershell
# Check binary in PATH
Get-Command garden-rake

# Check version
garden-rake --version

# Test discovery
garden-rake list
```

---

## Deployment Checklist

- [ ] Build release binaries (`build-dist.ps1 -Release`)
- [ ] Verify binary sizes (<20MB each)
- [ ] Test garden-moss binary on Linux (WSL or VM)
- [ ] Test garden-rake on Linux
- [ ] Test garden-rake.exe on Windows
- [ ] Copy binaries to `installer/bin/`
- [ ] Update NewStone.ps1 to reference binaries
- [ ] Test full USB deployment workflow
- [ ] Verify garden-moss.service starts on boot
- [ ] Verify garden-rake in PATH on Stone
- [ ] Test discovery: Windows client → Linux Stone

---

## Troubleshooting

### Build Errors

**Error: `cross-compilation requires target installed`**
```bash
rustup target add x86_64-pc-windows-gnu
rustup target add x86_64-pc-windows-msvc
```

**Error: `linker 'x86_64-w64-mingw32-gcc' not found`**
```bash
# Ubuntu/Debian
sudo apt install mingw-w64

# Arch
sudo pacman -S mingw-w64-gcc
```

### Binary Size Too Large

```bash
# Strip manually
strip target/release/garden-moss
strip target/release/garden-rake

# Use UPX compression (optional)
upx --best --lzma target/release/garden-moss
```

### WSL Build Issues

```powershell
# Ensure WSL has Rust installed
wsl curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# Sync files to WSL
wsl rsync -av /mnt/f/path/to/zen-garden ~/zen-garden
```

---

## Release Process

1. **Tag release**: `git tag -a v0.1.0 -m "Phase 2 Complete"`
2. **Push tag**: `git push origin v0.1.0`
3. **GitHub Actions builds binaries**
4. **Download artifacts from release**
5. **Update NewStone.ps1** with release URLs
6. **Test USB deployment**
7. **Publish release notes**
