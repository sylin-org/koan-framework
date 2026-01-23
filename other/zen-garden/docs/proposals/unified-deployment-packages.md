# Unified Deployment Packages

## Overview

This proposal unifies HTTP and SSH deployment methods under a single package-based approach. Both methods write the same staging file, processed identically on system restart.

## Goals

1. **Unified staging**: Both HTTP and SSH deployments write identical package files to the same staging location
2. **Platform-specific packages**: Separate packages for Linux and Windows targets
3. **Complete deployment**: Single package contains all components (moss, rake, lantern, manifests, offerings)
4. **Validation**: SHA256 checksums and architecture validation before deployment
5. **Atomic upgrades**: All-or-nothing deployment with rollback capability

---

## Package Format

### Naming Convention

```
zen-garden-{version}-{platform}-{arch}.{ext}
```

Examples:
- `zen-garden-0.1.202601231445-linux-amd64.tar.gz`
- `zen-garden-0.1.202601231445-windows-amd64.zip`

### Directory Structure

#### Linux Package (.tar.gz)

```
zen-garden-0.1.202601231445-linux-amd64/
├── package.json              # Manifest with checksums
├── bin/
│   ├── garden-moss           # Main daemon (ELF x86_64)
│   ├── garden-rake           # CLI tool (ELF x86_64)
│   └── garden-lantern        # Optional secondary daemon
├── lib/
│   └── (reserved for shared libraries)
├── offerings/
│   ├── databases/
│   │   ├── mongodb.yml
│   │   ├── postgresql.yml
│   │   └── ...
│   ├── messaging/
│   │   ├── rabbitmq.yml
│   │   └── ...
│   └── ...
└── config/
    └── (reserved for default configs)
```

#### Windows Package (.zip)

```
zen-garden-0.1.202601231445-windows-amd64/
├── package.json              # Manifest with checksums
├── bin/
│   ├── garden-moss.exe       # Main daemon (PE x86_64)
│   ├── garden-rake.exe       # CLI tool (PE x86_64)
│   └── garden-lantern.exe    # Optional secondary daemon
├── offerings/
│   └── (same structure as Linux)
└── config/
    └── (reserved for default configs)
```

---

## Package Manifest (package.json)

```json
{
  "version": "0.1.202601231445",
  "platform": "linux",
  "architecture": "amd64",
  "created": "2026-01-23T14:45:00Z",
  "components": {
    "garden-moss": {
      "path": "bin/garden-moss",
      "sha256": "a1b2c3d4e5f6...",
      "size": 12582912,
      "required": true
    },
    "garden-rake": {
      "path": "bin/garden-rake",
      "sha256": "b2c3d4e5f6a7...",
      "size": 8388608,
      "required": true
    },
    "garden-lantern": {
      "path": "bin/garden-lantern",
      "sha256": "c3d4e5f6a7b8...",
      "size": 6291456,
      "required": false
    }
  },
  "offerings": {
    "databases/mongodb.yml": "d4e5f6a7b8c9...",
    "databases/postgresql.yml": "e5f6a7b8c9d0...",
    "messaging/rabbitmq.yml": "f6a7b8c9d0e1..."
  },
  "minimumVersion": null,
  "notes": "Phase 0.1 - Core infrastructure"
}
```

### Field Descriptions

| Field | Type | Description |
|-------|------|-------------|
| `version` | string | Package version (major.minor.moment) |
| `platform` | string | Target platform: `linux` or `windows` |
| `architecture` | string | Target arch: `amd64`, `arm64` |
| `created` | string | ISO 8601 creation timestamp |
| `components` | object | Binary components with paths and checksums |
| `offerings` | object | Offering manifests with checksums |
| `minimumVersion` | string? | Minimum installed version for upgrade (null = any) |
| `notes` | string | Human-readable release notes |

---

## Staging Locations

### Linux
```
/var/lib/zen-garden/
├── staging/
│   └── pending-upgrade.tar.gz    # Staged package (both HTTP and SSH)
├── backups/
│   └── pre-upgrade-{timestamp}/  # Backup before upgrade
└── offerings/                     # Active offerings
```

### Windows
```
C:\ProgramData\ZenGarden\
├── staging\
│   └── pending-upgrade.zip       # Staged package (both HTTP and SSH)
├── backups\
│   └── pre-upgrade-{timestamp}\  # Backup before upgrade
└── offerings\                     # Active offerings
```

---

## Deployment Methods

### Method 1: HTTP API

```
POST /api/v1/stone:deploy
Content-Type: application/octet-stream
Content-Length: {size}
X-Package-SHA256: {sha256}

{binary package data}
```

Response (202 Accepted):
```json
{
  "status": "accepted",
  "message": "Package staged successfully",
  "version": "0.1.202601231445",
  "staged_path": "/var/lib/zen-garden/staging/pending-upgrade.tar.gz",
  "action": "restart_required"
}
```

Flow:
1. Receive package stream
2. Validate SHA256 header matches content
3. Extract and validate `package.json`
4. Verify platform/architecture match
5. Write to staging location
6. Return success (restart required for moss upgrades)

### Method 2: SSH/SCP

```powershell
# Copy package to staging location
pscp -i $keyFile $packagePath stone@${ip}:/var/lib/zen-garden/staging/pending-upgrade.tar.gz

# Trigger restart to apply
plink -i $keyFile stone@$ip "sudo systemctl restart garden-moss"
```

Both methods result in the same staging file, processed identically on restart.

---

## Upgrade Process

### Linux: garden-upgrade.sh

Runs as `ExecStartPre` in systemd unit:

```bash
#!/bin/bash
# garden-upgrade.sh - Process staged upgrade packages
set -euo pipefail

STAGING_DIR="/var/lib/zen-garden/staging"
PACKAGE_FILE="$STAGING_DIR/pending-upgrade.tar.gz"
BACKUP_DIR="/var/lib/zen-garden/backups"
TARGET_BIN="/usr/local/bin"
TARGET_OFFERINGS="/var/lib/zen-garden/offerings"

# Exit early if no pending upgrade
[[ -f "$PACKAGE_FILE" ]] || exit 0

log() { echo "[garden-upgrade] $1"; }

log "Found pending upgrade package"

# Create work directory
WORK_DIR=$(mktemp -d)
trap "rm -rf $WORK_DIR" EXIT

# Extract package
tar -xzf "$PACKAGE_FILE" -C "$WORK_DIR"
PACKAGE_DIR=$(find "$WORK_DIR" -maxdepth 1 -type d -name "zen-garden-*" | head -1)

# Validate package.json
if [[ ! -f "$PACKAGE_DIR/package.json" ]]; then
    log "ERROR: Invalid package - missing package.json"
    rm -f "$PACKAGE_FILE"
    exit 1
fi

# Validate platform
PLATFORM=$(jq -r '.platform' "$PACKAGE_DIR/package.json")
if [[ "$PLATFORM" != "linux" ]]; then
    log "ERROR: Wrong platform - expected linux, got $PLATFORM"
    rm -f "$PACKAGE_FILE"
    exit 1
fi

# Validate checksums for required components
log "Validating component checksums..."
for component in garden-moss garden-rake; do
    expected=$(jq -r ".components[\"$component\"].sha256" "$PACKAGE_DIR/package.json")
    actual=$(sha256sum "$PACKAGE_DIR/bin/$component" | cut -d' ' -f1)
    if [[ "$expected" != "$actual" ]]; then
        log "ERROR: Checksum mismatch for $component"
        rm -f "$PACKAGE_FILE"
        exit 1
    fi
done

# Validate ELF architecture
for binary in "$PACKAGE_DIR/bin/"*; do
    if ! file "$binary" | grep -q "ELF 64-bit LSB"; then
        log "ERROR: Invalid binary architecture: $(basename $binary)"
        rm -f "$PACKAGE_FILE"
        exit 1
    fi
done

# Create backup
TIMESTAMP=$(date +%Y%m%d%H%M%S)
BACKUP_PATH="$BACKUP_DIR/pre-upgrade-$TIMESTAMP"
mkdir -p "$BACKUP_PATH/bin"
log "Creating backup at $BACKUP_PATH"

for binary in garden-moss garden-rake garden-lantern; do
    [[ -f "$TARGET_BIN/$binary" ]] && cp "$TARGET_BIN/$binary" "$BACKUP_PATH/bin/"
done

# Deploy binaries
log "Deploying binaries..."
for binary in "$PACKAGE_DIR/bin/"*; do
    name=$(basename "$binary")
    cp "$binary" "$TARGET_BIN/$name"
    chmod 755 "$TARGET_BIN/$name"
    log "  Installed $name"
done

# Deploy offerings (merge, don't replace)
if [[ -d "$PACKAGE_DIR/offerings" ]]; then
    log "Deploying offerings..."
    mkdir -p "$TARGET_OFFERINGS"
    cp -r "$PACKAGE_DIR/offerings/"* "$TARGET_OFFERINGS/"
fi

# Cleanup
rm -f "$PACKAGE_FILE"
log "Upgrade complete - version $(jq -r '.version' "$PACKAGE_DIR/package.json")"
```

### Windows: garden-upgrade.ps1

Runs before service start:

```powershell
# garden-upgrade.ps1 - Process staged upgrade packages
$ErrorActionPreference = "Stop"

$STAGING_DIR = "C:\ProgramData\ZenGarden\staging"
$PACKAGE_FILE = Join-Path $STAGING_DIR "pending-upgrade.zip"
$BACKUP_DIR = "C:\ProgramData\ZenGarden\backups"
$TARGET_BIN = "C:\Program Files\ZenGarden\bin"
$TARGET_OFFERINGS = "C:\ProgramData\ZenGarden\offerings"

function Log($msg) { Write-Host "[garden-upgrade] $msg" }

# Exit early if no pending upgrade
if (-not (Test-Path $PACKAGE_FILE)) { exit 0 }

Log "Found pending upgrade package"

# Create work directory
$WORK_DIR = Join-Path $env:TEMP "zen-garden-upgrade-$(Get-Random)"
New-Item -ItemType Directory -Path $WORK_DIR -Force | Out-Null

try {
    # Extract package
    Expand-Archive -Path $PACKAGE_FILE -DestinationPath $WORK_DIR -Force
    $PACKAGE_DIR = Get-ChildItem $WORK_DIR -Directory | Where-Object { $_.Name -like "zen-garden-*" } | Select-Object -First 1

    # Validate package.json
    $manifestPath = Join-Path $PACKAGE_DIR.FullName "package.json"
    if (-not (Test-Path $manifestPath)) {
        Log "ERROR: Invalid package - missing package.json"
        Remove-Item $PACKAGE_FILE -Force
        exit 1
    }

    $manifest = Get-Content $manifestPath | ConvertFrom-Json

    # Validate platform
    if ($manifest.platform -ne "windows") {
        Log "ERROR: Wrong platform - expected windows, got $($manifest.platform)"
        Remove-Item $PACKAGE_FILE -Force
        exit 1
    }

    # Validate checksums
    Log "Validating component checksums..."
    foreach ($component in @("garden-moss", "garden-rake")) {
        $expected = $manifest.components.$component.sha256
        $binaryPath = Join-Path $PACKAGE_DIR.FullName "bin\$component.exe"
        $actual = (Get-FileHash $binaryPath -Algorithm SHA256).Hash.ToLower()
        if ($expected -ne $actual) {
            Log "ERROR: Checksum mismatch for $component"
            Remove-Item $PACKAGE_FILE -Force
            exit 1
        }
    }

    # Create backup
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $backupPath = Join-Path $BACKUP_DIR "pre-upgrade-$timestamp"
    New-Item -ItemType Directory -Path (Join-Path $backupPath "bin") -Force | Out-Null
    Log "Creating backup at $backupPath"

    Get-ChildItem "$TARGET_BIN\garden-*.exe" -ErrorAction SilentlyContinue | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $backupPath "bin")
    }

    # Deploy binaries
    Log "Deploying binaries..."
    New-Item -ItemType Directory -Path $TARGET_BIN -Force | Out-Null
    Get-ChildItem (Join-Path $PACKAGE_DIR.FullName "bin") -Filter "*.exe" | ForEach-Object {
        Copy-Item $_.FullName $TARGET_BIN -Force
        Log "  Installed $($_.Name)"
    }

    # Deploy offerings
    $offeringsSource = Join-Path $PACKAGE_DIR.FullName "offerings"
    if (Test-Path $offeringsSource) {
        Log "Deploying offerings..."
        New-Item -ItemType Directory -Path $TARGET_OFFERINGS -Force | Out-Null
        Copy-Item "$offeringsSource\*" $TARGET_OFFERINGS -Recurse -Force
    }

    # Cleanup
    Remove-Item $PACKAGE_FILE -Force
    Log "Upgrade complete - version $($manifest.version)"
}
finally {
    Remove-Item $WORK_DIR -Recurse -Force -ErrorAction SilentlyContinue
}
```

---

## Package Creation

Package creation is integrated into `dist.ps1` as Phase 3, running automatically after successful builds.

### dist.ps1 Package Phase

```powershell
# Build and create packages (default behavior)
.\dist.ps1

# Skip package creation
.\dist.ps1 -SkipPackages

# Build only Windows with packages
.\dist.ps1 -SkipLinux
```

### Output Structure

```
dist/
├── linux/
│   ├── garden-moss
│   └── garden-rake
├── windows/
│   ├── garden-moss.exe
│   └── garden-rake.exe
└── packages/
    ├── zen-garden-0.1.202601231445-linux-amd64.tar.gz
    └── zen-garden-0.1.202601231445-windows-amd64.zip
```

### Package Contents

Each package contains:
- `package.json` - Manifest with SHA256 checksums
- `bin/` - Platform binaries (garden-moss, garden-rake, garden-lantern)
- `offerings/` - Offering manifests (if present in workspace)

---

## Updated push2all.ps1

Add `-UsePackage` switch:

```powershell
param(
    # ... existing params ...
    [switch]$UsePackage    # Use package-based deployment instead of individual binaries
)

if ($UsePackage) {
    # Deploy using package
    $packagePath = Join-Path $DIST_DIR "packages" "zen-garden-$version-$platform-amd64.$ext"

    if ($Method -eq "HTTP") {
        # Stream package to /api/v1/stone:deploy
        $hash = (Get-FileHash $packagePath -Algorithm SHA256).Hash.ToLower()
        Invoke-RestMethod -Uri "http://${ip}:7280/api/v1/stone:deploy" `
            -Method POST `
            -InFile $packagePath `
            -ContentType "application/octet-stream" `
            -Headers @{ "X-Package-SHA256" = $hash }
    }
    else {
        # SSH: Copy package to staging
        & pscp -i $keyFile $packagePath "stone@${ip}:/var/lib/zen-garden/staging/pending-upgrade.tar.gz"
        & plink -i $keyFile "stone@$ip" "sudo systemctl restart garden-moss"
    }
}
```

---

## Implementation Plan

### Phase 1: Package Infrastructure ✅ Complete
1. ~~Create `dist-package.ps1` script~~ → Integrated into `dist.ps1` Phase 3
2. Define `package.json` schema ✅ (generated by dist.ps1)
3. Add package validation utilities ✅ (in upgrade scripts)

### Phase 2: Upgrade Scripts ✅ Complete
1. ✅ Create `garden-upgrade.sh` (Linux) - `installer/garden-upgrade.sh`
2. ⏳ Create `garden-upgrade.ps1` (Windows) - pending
3. ✅ Update `moss-update-helper.sh` to handle packages
4. Test backup/restore functionality

### Phase 3: API Endpoint ✅ Complete
1. ✅ Add `POST /api/v1/stone/deploy` endpoint - `src/moss/src/api/v1/stone.rs`
2. ✅ Implement binary package upload with SHA256 validation
3. ✅ Stage to `/var/lib/zen-garden/staging/pending-upgrade.tar.gz`
4. ✅ Auto-restart when package contains moss

### Phase 4: Deployment Script Updates (Pending)
1. Update `push2all.ps1` with `-UsePackage` option
2. Update `NewStone.ps1` to include upgrade scripts
3. Add package selection UI for partial upgrades

---

## Rollback Procedure

If an upgrade fails or causes issues:

### Linux
```bash
# Restore from backup
BACKUP="/var/lib/zen-garden/backups/pre-upgrade-{timestamp}"
sudo cp "$BACKUP/bin/"* /usr/local/bin/
sudo systemctl restart garden-moss
```

### Windows
```powershell
# Restore from backup
$BACKUP = "C:\ProgramData\ZenGarden\backups\pre-upgrade-{timestamp}"
Copy-Item "$BACKUP\bin\*" "C:\Program Files\ZenGarden\bin\" -Force
Restart-Service garden-moss
```

---

## Security Considerations

1. **Checksum Validation**: All binaries verified against SHA256 in manifest
2. **Architecture Validation**: ELF/PE headers checked before deployment
3. **Staging Permissions**:
   - Linux: `/var/lib/zen-garden/staging` is root-owned (755)
   - Windows: `C:\ProgramData\ZenGarden\staging` requires admin
4. **Backup Retention**: Keep last 3 backups, prune older automatically
5. **Transport**: HTTPS recommended for HTTP deployments

---

## Migration Path

### Existing Deployments

1. Deploy new moss version with package support (via old method)
2. Install `garden-upgrade.sh` to `/usr/local/bin/`
3. Update systemd unit to use new ExecStartPre
4. Future deployments use package method

### Fresh Deployments

1. `NewStone.ps1` creates USB with package-ready configuration
2. Initial install includes upgrade scripts
3. All subsequent upgrades use package method
