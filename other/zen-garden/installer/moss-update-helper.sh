#!/bin/bash
# Moss Update Helper - Copies staged binaries to final location
# This script runs as root via systemd ExecStartPre

STAGING_DIR="/home/stone/bin"
TARGET_DIR="/usr/local/bin"

# Check for staged garden-moss binary
if [ -f "$STAGING_DIR/garden-moss.staged" ]; then
    echo "[moss-update-helper] Found staged garden-moss binary, installing..."
    
    # Backup current binary
    if [ -f "$TARGET_DIR/garden-moss" ]; then
        cp "$TARGET_DIR/garden-moss" "$TARGET_DIR/garden-moss.backup" || true
    fi
    
    # Copy staged binary
    cp "$STAGING_DIR/garden-moss.staged" "$TARGET_DIR/garden-moss"
    chmod +x "$TARGET_DIR/garden-moss"
    
    # Remove staged file
    rm "$STAGING_DIR/garden-moss.staged"
    
    echo "[moss-update-helper] garden-moss binary updated successfully"
fi

# Check for staged garden-rake binary
if [ -f "$STAGING_DIR/garden-rake.staged" ]; then
    echo "[moss-update-helper] Found staged garden-rake binary, installing..."
    
    # Backup current binary
    if [ -f "$TARGET_DIR/garden-rake" ]; then
        cp "$TARGET_DIR/garden-rake" "$TARGET_DIR/garden-rake.backup" || true
    fi
    
    # Copy staged binary
    cp "$STAGING_DIR/garden-rake.staged" "$TARGET_DIR/garden-rake"
    chmod +x "$TARGET_DIR/garden-rake"
    
    # Remove staged file
    rm "$STAGING_DIR/garden-rake.staged"
    
    echo "[moss-update-helper] garden-rake binary updated successfully"
fi

exit 0
