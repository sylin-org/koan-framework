#!/bin/bash
# garden-upgrade.sh - Process staged upgrade packages
#
# This script runs as ExecStartPre in the systemd unit.
# It processes pending-upgrade.tar.gz packages deposited by:
#   - HTTP API: POST /api/v1/stone/deploy
#   - SSH: Direct copy to /var/lib/zen-garden/staging/
#
# Package format:
#   zen-garden-{version}-linux-amd64/
#   ├── package.json          # Manifest with checksums
#   ├── bin/
#   │   ├── garden-moss
#   │   ├── garden-rake
#   │   └── garden-lantern
#   ├── scripts/
#   │   ├── moss-update-helper.sh
#   │   └── garden-upgrade.sh
#   └── manifests/
#       └── ...

set -euo pipefail

STAGING_DIR="/var/lib/zen-garden/staging"
PACKAGE_FILE="$STAGING_DIR/pending-upgrade.tar.gz"
BACKUP_DIR="/var/lib/zen-garden/backups"
TARGET_BIN="/usr/local/bin"
TARGET_MANIFESTS="/var/lib/zen-garden/manifests"
MAX_BACKUPS=3

log() {
    echo "[garden-upgrade] $1"
}

error() {
    echo "[garden-upgrade] ERROR: $1" >&2
}

# Exit early if no pending upgrade
if [[ ! -f "$PACKAGE_FILE" ]]; then
    exit 0
fi

log "Found pending upgrade package: $PACKAGE_FILE"

# Create work directory
WORK_DIR=$(mktemp -d)
trap 'rm -rf "$WORK_DIR"' EXIT

# Extract package
log "Extracting package..."
if ! tar -xzf "$PACKAGE_FILE" -C "$WORK_DIR"; then
    error "Failed to extract package"
    rm -f "$PACKAGE_FILE"
    exit 1
fi

# Find the extracted package directory
PACKAGE_DIR=$(find "$WORK_DIR" -maxdepth 1 -type d -name "zen-garden-*" | head -1)
if [[ -z "$PACKAGE_DIR" || ! -d "$PACKAGE_DIR" ]]; then
    error "Invalid package structure - no zen-garden-* directory found"
    rm -f "$PACKAGE_FILE"
    exit 1
fi

log "Package directory: $(basename "$PACKAGE_DIR")"

# Validate package.json exists
MANIFEST_FILE="$PACKAGE_DIR/package.json"
if [[ ! -f "$MANIFEST_FILE" ]]; then
    error "Invalid package - missing package.json"
    rm -f "$PACKAGE_FILE"
    exit 1
fi

# Extract version and platform from manifest
VERSION=$(jq -r '.version // "unknown"' "$MANIFEST_FILE")
PLATFORM=$(jq -r '.platform // "unknown"' "$MANIFEST_FILE")

log "Package version: $VERSION, platform: $PLATFORM"

# Validate platform
if [[ "$PLATFORM" != "linux" ]]; then
    error "Wrong platform - expected linux, got $PLATFORM"
    rm -f "$PACKAGE_FILE"
    exit 1
fi

# Validate component checksums
log "Validating component checksums..."
COMPONENTS=$(jq -r '.components | keys[]' "$MANIFEST_FILE" 2>/dev/null || echo "")

for component in $COMPONENTS; do
    expected=$(jq -r ".components[\"$component\"].sha256" "$MANIFEST_FILE")
    component_path=$(jq -r ".components[\"$component\"].path" "$MANIFEST_FILE")
    full_path="$PACKAGE_DIR/$component_path"

    if [[ -f "$full_path" ]]; then
        actual=$(sha256sum "$full_path" | cut -d' ' -f1)
        if [[ "$expected" != "$actual" ]]; then
            error "Checksum mismatch for $component"
            error "  Expected: $expected"
            error "  Actual:   $actual"
            rm -f "$PACKAGE_FILE"
            exit 1
        fi
        log "  ✓ $component checksum valid"
    else
        required=$(jq -r ".components[\"$component\"].required" "$MANIFEST_FILE")
        if [[ "$required" == "true" ]]; then
            error "Required component missing: $component_path"
            rm -f "$PACKAGE_FILE"
            exit 1
        fi
        log "  - $component not present (optional)"
    fi
done

# Validate script checksums (if present)
SCRIPTS=$(jq -r '.scripts | keys[]' "$MANIFEST_FILE" 2>/dev/null || echo "")
if [[ -n "$SCRIPTS" ]]; then
    log "Validating script checksums..."
    for script in $SCRIPTS; do
        expected=$(jq -r ".scripts[\"$script\"].sha256" "$MANIFEST_FILE")
        script_path=$(jq -r ".scripts[\"$script\"].path" "$MANIFEST_FILE")
        full_path="$PACKAGE_DIR/$script_path"

        if [[ -f "$full_path" ]]; then
            actual=$(sha256sum "$full_path" | cut -d' ' -f1)
            if [[ "$expected" != "$actual" ]]; then
                error "Checksum mismatch for script $script"
                error "  Expected: $expected"
                error "  Actual:   $actual"
                rm -f "$PACKAGE_FILE"
                exit 1
            fi
            log "  ✓ $script checksum valid"
        else
            log "  - $script not present in package"
        fi
    done
fi

# Validate binary architectures
log "Validating binary architectures..."
for binary in "$PACKAGE_DIR/bin/"*; do
    if [[ -f "$binary" ]]; then
        if ! file "$binary" | grep -qE "ELF 64-bit LSB"; then
            error "Invalid binary architecture: $(basename "$binary")"
            rm -f "$PACKAGE_FILE"
            exit 1
        fi
    fi
done
log "All binaries are valid ELF 64-bit"

# Create backup
TIMESTAMP=$(date +%Y%m%d%H%M%S)
BACKUP_PATH="$BACKUP_DIR/pre-upgrade-$TIMESTAMP"
mkdir -p "$BACKUP_PATH/bin"

log "Creating backup at $BACKUP_PATH"
for binary in garden-moss garden-rake garden-lantern; do
    if [[ -f "$TARGET_BIN/$binary" ]]; then
        cp "$TARGET_BIN/$binary" "$BACKUP_PATH/bin/"
        log "  Backed up $binary"
    fi
done

# Backup current manifests if they exist
if [[ -d "$TARGET_MANIFESTS" ]]; then
    cp -r "$TARGET_MANIFESTS" "$BACKUP_PATH/manifests"
    log "  Backed up manifests"
fi

# Save package.json to backup for reference
cp "$MANIFEST_FILE" "$BACKUP_PATH/"

# Prune old backups (keep only MAX_BACKUPS)
BACKUP_COUNT=$(find "$BACKUP_DIR" -maxdepth 1 -type d -name "pre-upgrade-*" | wc -l)
if [[ $BACKUP_COUNT -gt $MAX_BACKUPS ]]; then
    log "Pruning old backups (keeping $MAX_BACKUPS)..."
    find "$BACKUP_DIR" -maxdepth 1 -type d -name "pre-upgrade-*" | sort | head -n -$MAX_BACKUPS | xargs rm -rf
fi

# Deploy binaries
log "Deploying binaries..."
for binary in "$PACKAGE_DIR/bin/"*; do
    if [[ -f "$binary" ]]; then
        name=$(basename "$binary")
        cp "$binary" "$TARGET_BIN/$name"
        chmod 755 "$TARGET_BIN/$name"
        log "  Installed $name"
    fi
done

# Deploy manifests (merge, don't replace)
if [[ -d "$PACKAGE_DIR/manifests" ]]; then
    log "Deploying manifests..."
    mkdir -p "$TARGET_MANIFESTS"
    cp -r "$PACKAGE_DIR/manifests/"* "$TARGET_MANIFESTS/"
    log "  Manifests updated"
fi

# Deploy scripts (install to /usr/local/bin)
if [[ -d "$PACKAGE_DIR/scripts" ]]; then
    log "Deploying scripts..."
    for script in "$PACKAGE_DIR/scripts/"*.sh; do
        if [[ -f "$script" ]]; then
            name=$(basename "$script")
            # Backup existing script
            if [[ -f "$TARGET_BIN/$name" ]]; then
                mkdir -p "$BACKUP_PATH/scripts"
                cp "$TARGET_BIN/$name" "$BACKUP_PATH/scripts/"
            fi
            cp "$script" "$TARGET_BIN/$name"
            chmod 755 "$TARGET_BIN/$name"
            log "  Installed $name"
        fi
    done
fi

# Cleanup staged package
rm -f "$PACKAGE_FILE"

log "Upgrade complete - version $VERSION"
log "Backup available at: $BACKUP_PATH"

exit 0
