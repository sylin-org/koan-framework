#!/bin/bash
set -e

echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║   Running First-Boot Test                   ║"
echo "╚══════════════════════════════════════════════╝"
echo ""

# Check initial config
echo "  Initial Configuration:"
echo "  ---------------------"
cat /etc/zen-garden/moss.toml | grep stone_name
echo ""

# Make binary executable
chmod +x /test/garden-moss

# Show what would happen (dry run without actual system changes)
echo "  [INFO] Testing first-run detection..."
echo ""

# Run moss with environment to trigger first-run
echo "  [INFO] Launching moss with stone-new-testguid name..."
timeout 10 /test/garden-moss --stone-name stone-new-testguid 2>&1 | head -80 || true

echo ""
echo "  [INFO] Test completed - check output above for:"
echo "    - First-run detection message"
echo "    - Console initialization attempts"
echo "    - Name generation logic"
echo ""
