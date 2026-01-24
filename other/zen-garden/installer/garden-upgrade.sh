#!/bin/bash
# garden-upgrade.sh - Install pre-validated binaries from staging
#
# This script runs as ExecStartPre in the systemd unit.
# It installs binaries that have already been validated by the Moss service.
#
# Staging structure (created by Moss):
#   /var/lib/zen-garden/staging/validated/
#   ├── bin/
#   │   ├── garden-moss
#   │   ├── garden-rake
#   │   └── garden-lantern (optional)
#   └── scripts/
#       ├── moss-update-helper.sh
#       └── garden-upgrade.sh
#
# SAFETY: This script can upgrade itself. Linux allows overwriting a running
# script's file because the running process holds the inode. Next restart will
# use the new version.

set -euo pipefail

STAGING_DIR="/var/lib/zen-garden/staging/validated"
TARGET_BIN="/usr/local/bin"

log() {
    echo "[garden-upgrade] $1"
}

# Exit early if no staged content
if [[ ! -d "$STAGING_DIR" ]]; then
    exit 0
fi

if [[ ! -d "$STAGING_DIR/bin" ]] && [[ ! -d "$STAGING_DIR/scripts" ]]; then
    log "Staging directory exists but is empty"
    exit 0
fi

log "Installing staged binaries..."

# Install scripts FIRST (so we can upgrade ourselves atomically)
if [[ -d "$STAGING_DIR/scripts" ]]; then
    log "Installing scripts (including potential self-upgrade)..."
    for script in "$STAGING_DIR/scripts/"*.sh; do
        if [[ -f "$script" ]]; then
            name=$(basename "$script")
            # Copy new version over old version (Linux allows this for running scripts)
            cp "$script" "$TARGET_BIN/$name"
            chmod 755 "$TARGET_BIN/$name"
            log "  Installed $name"
        fi
    done
fi

# Install binaries (moss, rake, lantern, etc.)
if [[ -d "$STAGING_DIR/bin" ]]; then
    log "Installing binaries..."
    for binary in "$STAGING_DIR/bin/"*; do
        if [[ -f "$binary" ]]; then
            name=$(basename "$binary")
            cp "$binary" "$TARGET_BIN/$name"
            chmod 755 "$TARGET_BIN/$name"
            log "  Installed $name"
        fi
    done
fi

# Cleanup staging
rm -rf "$STAGING_DIR"
log "Installation complete"

exit 0
