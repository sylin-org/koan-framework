# .NET 10 RC 1 Installation Guide

## Overview

This guide covers installation of .NET 10 Release Candidate 1 for Koan Framework migration testing.

**Important**: .NET 10 RC 1 is a prerelease version with go-live support. It can be used in production, but be aware of potential stability issues.

---

## Prerequisites

- Remove or uninstall .NET 9 SDK (optional, but recommended to avoid conflicts)
- Ensure Visual Studio 2026 Insiders is installed (if using Visual Studio)
- Administrator/elevated permissions for installation

---

## Installation Steps

### Windows

#### Option 1: Using winget (Recommended)

```powershell
# Search for available .NET 10 RC versions
winget search Microsoft.DotNet.SDK.Preview

# Install .NET 10 RC 1 SDK
winget install Microsoft.DotNet.SDK.10.Preview

# Or specify exact version
winget install Microsoft.DotNet.SDK.10 --version 10.0.100-rc.1.25451.107
```

#### Option 2: Manual Download

1. Visit: https://dotnet.microsoft.com/download/dotnet/10.0
2. Download **.NET 10 RC 1 SDK** for Windows x64
3. Run the installer
4. Follow installation wizard

#### Option 3: Using Chocolatey

```powershell
choco install dotnet-sdk --version=10.0.100-rc.1 --pre
```

### Linux (Ubuntu/Debian)

```bash
# Add Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Update package list
sudo apt-get update

# Install .NET 10 RC 1 SDK
sudo apt-get install -y dotnet-sdk-10.0
```

### macOS

#### Option 1: Using Homebrew

```bash
# Tap the preview cask
brew tap isen-ng/dotnet-sdk-versions

# Install .NET 10 RC
brew install --cask dotnet-sdk-preview
```

#### Option 2: Manual Download

1. Visit: https://dotnet.microsoft.com/download/dotnet/10.0
2. Download **.NET 10 RC 1 SDK** for macOS (x64 or ARM64)
3. Open the .pkg file
4. Follow installation wizard

---

## Verification

After installation, verify the SDK is correctly installed:

```bash
# Check installed SDK versions
dotnet --list-sdks

# Expected output should include:
# 10.0.100-rc.1.25451.107 [C:\Program Files\dotnet\sdk] (Windows)
# 10.0.100-rc.1.25451.107 [/usr/share/dotnet/sdk] (Linux)

# Check runtime versions
dotnet --list-runtimes

# Expected runtimes:
# Microsoft.NETCore.App 10.0.0-rc.1.25451.107
# Microsoft.AspNetCore.App 10.0.0-rc.1.25451.107
# Microsoft.WindowsDesktop.App 10.0.0-rc.1.25451.107 (Windows only)

# Verify active SDK
dotnet --version

# Should output: 10.0.100-rc.1.25451.107
```

---

## Docker Installation

### Update Docker Base Images

.NET 10 RC 1 Docker images are available from Microsoft Container Registry:

```dockerfile
# SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:10.0-rc

# Runtime image for ASP.NET Core apps
FROM mcr.microsoft.com/dotnet/aspnet:10.0-rc

# Runtime image for console apps
FROM mcr.microsoft.com/dotnet/runtime:10.0-rc
```

### Pull Images Manually

```bash
# Pull SDK image
docker pull mcr.microsoft.com/dotnet/sdk:10.0-rc

# Pull ASP.NET Core runtime
docker pull mcr.microsoft.com/dotnet/aspnet:10.0-rc

# Verify images
docker images | grep dotnet
```

---

## IDE Configuration

### Visual Studio 2026 Insiders

1. Download Visual Studio 2026 Insiders from: https://visualstudio.microsoft.com/vs/preview/
2. Install with .NET desktop development and ASP.NET workloads
3. Visual Studio will automatically detect .NET 10 RC 1 SDK
4. Restart Visual Studio after SDK installation

### Visual Studio Code

1. Install latest VS Code: https://code.visualstudio.com/
2. Install C# Dev Kit extension
3. Install .NET 10 RC 1 SDK (as above)
4. Reload VS Code
5. Verify SDK detection:
   - Open Command Palette (Ctrl+Shift+P)
   - Type ".NET: Show SDK"
   - Confirm 10.0.100-rc.1 appears

### JetBrains Rider

1. Update Rider to 2024.3 or later
2. Install .NET 10 RC 1 SDK (as above)
3. Restart Rider
4. Go to Settings → Build, Execution, Deployment → Toolset and Build
5. Verify .NET 10 SDK is detected
6. Set as default SDK for new projects

---

## Global.json Configuration

### Option 1: Pin to .NET 10 RC 1

Create `global.json` in repository root to pin SDK version:

```json
{
  "sdk": {
    "version": "10.0.100-rc.1.25451.107",
    "rollForward": "latestMinor",
    "allowPrerelease": true
  }
}
```

### Option 2: Allow Any .NET 10 RC

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestPatch",
    "allowPrerelease": true
  }
}
```

### Option 3: No Pinning (Use Latest Available)

Do not create `global.json` - the build will use the latest installed SDK.

**Recommendation for Koan Framework**: Use Option 1 to ensure consistent builds across team members.

---

## NuGet Configuration

### Enable Prerelease Packages

Update `NuGet.config` or use command line flags:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

### Install Prerelease Packages

```bash
# Restore with prerelease packages
dotnet restore --use-lock-file

# Add prerelease package
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0-rc.1.25451.107

# Update all packages to prerelease
dotnet list package --outdated --include-prerelease
```

---

## CI/CD Integration

### GitHub Actions

```yaml
name: .NET 10 RC Build

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET 10 RC
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'
        dotnet-quality: 'rc'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore

    - name: Test
      run: dotnet test --no-build --verbosity normal
```

### Azure Pipelines

```yaml
trigger:
- feature/dotnet-10-rc1-migration

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: UseDotNet@2
  displayName: 'Install .NET 10 RC SDK'
  inputs:
    version: '10.0.x'
    includePreviewVersions: true
    performMultiLevelLookup: true

- script: dotnet restore
  displayName: 'Restore packages'

- script: dotnet build --configuration Release
  displayName: 'Build solution'

- script: dotnet test --configuration Release --no-build
  displayName: 'Run tests'
```

---

## Troubleshooting

### Issue: SDK Not Found After Installation

**Solution**:
```bash
# Verify PATH includes .NET SDK location
# Windows: C:\Program Files\dotnet
# Linux/macOS: /usr/local/share/dotnet or /usr/share/dotnet

# Windows - Add to PATH if missing
$env:PATH += ";C:\Program Files\dotnet"

# Linux/macOS - Add to PATH
export PATH=$PATH:/usr/share/dotnet
```

### Issue: Multiple SDK Versions Conflict

**Solution**:
```bash
# List all installed SDKs
dotnet --list-sdks

# Use global.json to pin version (see above)

# Or uninstall conflicting SDKs
# Windows: Settings → Apps → .NET SDK
# Linux: sudo apt remove dotnet-sdk-9.0
```

### Issue: Docker Build Fails with "SDK Not Found"

**Solution**:
```dockerfile
# Explicitly specify RC tag
FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1-alpine

# Or use specific build number
FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1.25451.107-bookworm-slim
```

### Issue: NuGet Package Restore Fails

**Solution**:
```bash
# Clear NuGet cache
dotnet nuget locals all --clear

# Restore with verbose logging
dotnet restore -v detailed

# Check package source
dotnet nuget list source
```

### Issue: Visual Studio Doesn't Detect .NET 10 RC

**Solution**:
1. Restart Visual Studio
2. Verify SDK installation: `dotnet --version`
3. Update Visual Studio to latest preview version
4. Repair .NET SDK installation

---

## Rollback Procedure

If issues arise and you need to rollback to .NET 9:

### Windows

```powershell
# Uninstall .NET 10 RC
winget uninstall Microsoft.DotNet.SDK.10

# Reinstall .NET 9
winget install Microsoft.DotNet.SDK.9
```

### Linux

```bash
# Remove .NET 10
sudo apt remove dotnet-sdk-10.0

# Install .NET 9
sudo apt install dotnet-sdk-9.0
```

### Docker

```dockerfile
# Revert Dockerfiles to .NET 9 images
FROM mcr.microsoft.com/dotnet/sdk:9.0
FROM mcr.microsoft.com/dotnet/aspnet:9.0
```

### Repository

```bash
# Switch back to main/dev branch
git checkout dev

# Or revert changes
git reset --hard origin/dev
```

---

## Next Steps

After successful installation:

1. ✅ Verify SDK version: `dotnet --version`
2. ✅ Review [DOTNET-10-MIGRATION-PLAN.md](./DOTNET-10-MIGRATION-PLAN.md)
3. ✅ Update project files to target `net10.0`
4. ✅ Update package references to `10.0.0-rc.1.*` versions
5. ✅ Run initial build: `dotnet build`
6. ✅ Execute test suite: `dotnet test`

---

## References

- [.NET 10 RC 1 Announcement](https://devblogs.microsoft.com/dotnet/dotnet-10-rc-1/)
- [.NET 10 Breaking Changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0)
- [.NET 10 Downloads](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Hub - .NET Images](https://hub.docker.com/_/microsoft-dotnet)
- [Visual Studio 2026 Preview](https://visualstudio.microsoft.com/vs/preview/)

---

**Document Version**: 1.0
**Last Updated**: 2025-10-01
**Migration Branch**: `feature/dotnet-10-rc1-migration`
