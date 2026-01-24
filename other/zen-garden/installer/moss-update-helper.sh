#!/bin/bash
# moss-update-helper.sh - Check for staged binaries before Moss starts
#
# This script runs as ExecStartPre in the systemd unit (before garden-upgrade.sh).
# It checks if there are staged binaries from a previous upgrade attempt.
# The actual installation is handled by garden-upgrade.sh.

set -euo pipefail

STAGING_DIR="/var/lib/zen-garden/staging/validated"

log() {
    echo "[moss-update-helper] $1"
}

log "Starting update check..."

# Check if we have staged binaries
if [[ -d "$STAGING_DIR/bin" ]]; then
    log "Found staged binaries - will be installed by garden-upgrade.sh"
else
    log "No staged binaries found"
fi

exit 0
                return 0
            else
                log "ERROR: Failed to copy ${staged_path} to ${target_path}"
                rm -f "$staged_path"
                return 1
            fi
        fi
    done

    # No staged file found (normal case)
    return 0
}

# Ensure staging directories exist with correct permissions
ensure_staging_dirs() {
    # Root-owned staging for HTTP API
    if [ ! -d "/var/lib/zen-garden/staging" ]; then
        mkdir -p /var/lib/zen-garden/staging
        chmod 755 /var/lib/zen-garden/staging
        log "Created /var/lib/zen-garden/staging"
    fi

    # Stone-owned staging for SSH (should exist, but ensure it does)
    if [ ! -d "/home/stone/bin" ]; then
        mkdir -p /home/stone/bin
        chown stone:stone /home/stone/bin
        chmod 755 /home/stone/bin
        log "Created /home/stone/bin"
    fi
}

# Clean up any stale staged files older than 1 hour (failed deployments)
cleanup_stale_staged() {
    for staging_dir in "${STAGING_DIRS[@]}"; do
        if [ -d "$staging_dir" ]; then
            find "$staging_dir" -name "*.staged" -mmin +60 -delete 2>/dev/null || true
        fi
    done
}

# Process package-based upgrade
process_package_upgrade() {
    if [[ -f "$PACKAGE_FILE" ]]; then
        log "Found upgrade package: $PACKAGE_FILE"

        # Try to run garden-upgrade.sh from known locations
        local upgrade_script=""
        for location in "/usr/local/bin/garden-upgrade.sh" "$SCRIPT_DIR/garden-upgrade.sh"; do
            if [[ -x "$location" ]]; then
                upgrade_script="$location"
                break
            fi
        done

        if [[ -n "$upgrade_script" ]]; then
            log "Running package upgrade via $upgrade_script"
            if "$upgrade_script"; then
                log "Package upgrade completed successfully"
                return 0
            else
                log "ERROR: Package upgrade failed"
                return 1
            fi
        else
            # Inline package processing if garden-upgrade.sh not found
            log "garden-upgrade.sh not found, processing package inline..."
            process_package_inline
        fi
    fi
    return 0
}

# Inline package processing (fallback if garden-upgrade.sh not installed)
process_package_inline() {
    local work_dir
    work_dir=$(mktemp -d)
    trap 'rm -rf "$work_dir"' RETURN

    # Extract package
    if ! tar -xzf "$PACKAGE_FILE" -C "$work_dir"; then
        log "ERROR: Failed to extract package"
        rm -f "$PACKAGE_FILE"
        return 1
    fi

    # Find package directory
    local pkg_dir
    pkg_dir=$(find "$work_dir" -maxdepth 1 -type d -name "zen-garden-*" | head -1)
    if [[ -z "$pkg_dir" ]]; then
        log "ERROR: Invalid package structure"
        rm -f "$PACKAGE_FILE"
        return 1
    fi

    # Deploy binaries
    if [[ -d "$pkg_dir/bin" ]]; then
        for binary in "$pkg_dir/bin/"*; do
            if [[ -f "$binary" ]]; then
                local name
                name=$(basename "$binary")
                cp "$binary" "$TARGET_DIR/$name"
                chmod 755 "$TARGET_DIR/$name"
                log "Installed $name"
            fi
        done
    fi

    # Deploy manifests
    if [[ -d "$pkg_dir/manifests" ]]; then
        mkdir -p /var/lib/zen-garden/manifests
        cp -r "$pkg_dir/manifests/"* /var/lib/zen-garden/manifests/
        log "Updated manifests"
    fi

    # Deploy scripts (install to /usr/local/bin, make executable)
    if [[ -d "$pkg_dir/scripts" ]]; then
        for script in "$pkg_dir/scripts/"*.sh; do
            if [[ -f "$script" ]]; then
                local name
                name=$(basename "$script")
                cp "$script" "$TARGET_DIR/$name"
                chmod 755 "$TARGET_DIR/$name"
                log "Installed script $name"
            fi
        done
    fi

    rm -f "$PACKAGE_FILE"
    log "Package upgrade complete"
}

# Main
main() {
    log "Starting update check..."

    ensure_staging_dirs

    # First, process package-based upgrades (new method)
    process_package_upgrade

    # Then, process legacy staged binaries (backwards compatibility)
    cleanup_stale_staged
    process_staged_binary "garden-moss"
    process_staged_binary "garden-rake"

    log "Update check complete"
}

main
exit 0
