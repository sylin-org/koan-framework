#!/bin/bash
# Moss Update Helper - Copies staged binaries to final location
# This script runs as root via systemd ExecStartPre
#
# Supports two staging locations to avoid permission conflicts:
# - /var/lib/zen-garden/staging/ : HTTP API deployments (root writes here)
# - /home/stone/bin/             : SSH deployments (stone user writes here)

set -euo pipefail

# Staging locations (checked in priority order)
STAGING_DIRS=(
    "/var/lib/zen-garden/staging"  # HTTP API (root-owned)
    "/home/stone/bin"               # SSH (stone-owned)
)
TARGET_DIR="/usr/local/bin"

log() {
    echo "[moss-update-helper] $1"
}

# Process a staged binary from any staging location
# Args: $1 = component name (garden-moss or garden-rake)
process_staged_binary() {
    local component="$1"
    local staged_file="${component}.staged"
    local target_path="${TARGET_DIR}/${component}"

    for staging_dir in "${STAGING_DIRS[@]}"; do
        local staged_path="${staging_dir}/${staged_file}"

        if [ -f "$staged_path" ]; then
            log "Found staged ${component} in ${staging_dir}"

            # Validate it's actually an executable
            if ! file "$staged_path" | grep -qE '(ELF|executable)'; then
                log "WARNING: ${staged_path} doesn't appear to be a valid executable, skipping"
                rm -f "$staged_path"
                continue
            fi

            # Backup current binary
            if [ -f "$target_path" ]; then
                cp "$target_path" "${target_path}.backup" 2>/dev/null || true
                log "Backed up current ${component}"
            fi

            # Copy staged binary to target
            if cp "$staged_path" "$target_path"; then
                chmod +x "$target_path"
                rm -f "$staged_path"
                log "${component} updated successfully from ${staging_dir}"
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

# Main
main() {
    log "Starting update check..."

    ensure_staging_dirs
    cleanup_stale_staged

    # Process each component
    process_staged_binary "garden-moss"
    process_staged_binary "garden-rake"

    log "Update check complete"
}

main
exit 0
