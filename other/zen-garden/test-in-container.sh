#!/bin/bash
set -e

echo ''
echo '╔══════════════════════════════════════════════╗'
echo '║   First-Boot Console Test in Build Container║'
echo '╚══════════════════════════════════════════════╝'
echo ''

# Create test moss.toml with temporary name
mkdir -p /etc/zen-garden
cat > /etc/zen-garden/moss.toml <<TOML
stone_name = "stone-new-testguid"
port = 7185
log_level = "info"
TOML

echo '  Test Configuration:'
echo '  -------------------'
cat /etc/zen-garden/moss.toml
echo ''

# Run moss with first-boot name (will fail on system commands but we'll see the output)
echo '  [INFO] Launching moss with first-boot name...'
echo '  [INFO] (Expect failures for hostnamectl/avahi - this is normal in container)'
echo ''

timeout 10 /build/target/debug/garden-moss --stone-name stone-new-testguid 2>&1 | head -150 || true

echo ''
echo '  ═══════════════════════════════════════════════'
echo '  Test Complete - Key Points to Verify:'
echo '  ═══════════════════════════════════════════════'
echo '  ✓ "First run detected" message appears'
echo '  ✓ Console header boxes display correctly'
echo '  ✓ Name generation logic attempts to run'
echo '  ✓ No Rust panics or segfaults'
echo ''
