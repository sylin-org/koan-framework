# Build Optimization Guide

## Overview

The Zen Garden build scripts now **default to optimized release builds** for production-ready, size-optimal binaries.

## Build Profiles

### Release (Default)
Optimized for **size and performance**. Use for distribution and production deployment.

```powershell
.\installer\dist.ps1                    # All platforms
.\installer\build-linux.ps1             # Linux only
.\installer\build-windows.ps1           # Windows only
```

**Optimization settings** (from [Cargo.toml](../Cargo.toml#L38-L41)):
- `strip = true` - Removes debug symbols (smaller binaries)
- `lto = true` - Link-time optimization (better performance, smaller size)
- `codegen-units = 1` - Maximum optimization (slower compile, best results)

**Expected binary sizes** (release, x86_64):
- `garden-moss`: ~8-12 MB (daemon with full HTTP stack)
- `garden-rake`: ~4-6 MB (CLI tool)
- `garden-lantern`: ~5-8 MB (service registry)

### Debug Mode
Faster compilation for **development iteration**. Larger binaries with debug symbols.

```powershell
.\installer\dist.ps1 -Debug
.\installer\build-linux.ps1 -Debug
.\installer\build-windows.ps1 -Debug
```

**When to use:**
- Rapid iteration during development
- Debugging with full symbol information
- Running tests locally

**Expected binary sizes** (debug, x86_64):
- 3-4x larger than release builds
- Full stack traces and debug symbols included

## Deployment

The `push2all.ps1` script expects **release binaries** from `dist/`:
- Automatically detects platform (Linux/Windows) for each stone
- Deploys appropriately sized binaries (release mode)
- Configured for 200 MB body limit (handles base64 overhead)

```powershell
# Build optimized binaries for all platforms
.\installer\dist.ps1

# Deploy to all discovered stones
.\installer\push2all.ps1
```

## Size Verification

Check binary sizes after build:

```powershell
# Windows
Get-ChildItem .\dist\windows\*.exe | Format-Table Name, @{L="Size (MB)";E={[math]::Round($_.Length/1MB,2)}}

# Linux
Get-ChildItem .\dist\linux\garden-* | Format-Table Name, @{L="Size (MB)";E={[math]::Round($_.Length/1MB,2)}}
```

## Build Performance

### Release Builds
- **Compile time**: 2-5 minutes (full clean build)
- **Incremental**: 30-90 seconds
- **Docker (Linux)**: +30-60 seconds (container overhead)

### Debug Builds
- **Compile time**: 30-90 seconds (full clean build)
- **Incremental**: 10-30 seconds

## Troubleshooting

### Binaries too large
- Verify release mode: Check script output for "Build Type: Release (optimized)"
- Confirm Cargo.toml profile settings are present
- Check for debug symbols: `file garden-moss` (should show "stripped")

### Upload failures
- Ensure using release builds (debug binaries may exceed 200 MB limit with base64 encoding)
- Verify body limit in moss: `DefaultBodyLimit::max(200 * 1024 * 1024)`

## References

- Cargo profiles: https://doc.rust-lang.org/cargo/reference/profiles.html
- LTO documentation: https://doc.rust-lang.org/cargo/reference/profiles.html#lto
- Size optimization: https://github.com/johnthagen/min-sized-rust
