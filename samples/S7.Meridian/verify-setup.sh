#!/bin/bash

# S7.Meridian Phase 1 Setup Verification Script
# This script checks that all prerequisites are ready for testing

set -e

echo "======================================"
echo "S7.Meridian Phase 1 Setup Verification"
echo "======================================"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check counter
PASSED=0
FAILED=0

check_step() {
    local name=$1
    local command=$2

    echo -n "Checking $name... "
    if eval "$command" > /dev/null 2>&1; then
        echo -e "${GREEN}✓ PASS${NC}"
        ((PASSED++))
        return 0
    else
        echo -e "${RED}✗ FAIL${NC}"
        ((FAILED++))
        return 1
    fi
}

# 1. Check Ollama
echo "=== Ollama Checks ==="
check_step "Ollama installed" "which ollama"
check_step "granite3.3:8b model available" "ollama list | grep -q 'granite3.3:8b'"

if ollama list | grep -q 'granite3.3:8b'; then
    echo -n "Testing Ollama response... "
    RESPONSE=$(ollama run granite3.3:8b "What is 2+2? Answer with just the number." 2>/dev/null | head -1)
    if [[ "$RESPONSE" == *"4"* ]]; then
        echo -e "${GREEN}✓ PASS${NC} (Response: $RESPONSE)"
        ((PASSED++))
    else
        echo -e "${YELLOW}⚠ WARN${NC} (Unexpected response: $RESPONSE)"
    fi
fi
echo ""

# 2. Check MongoDB
echo "=== MongoDB Checks ==="
if command -v mongosh &> /dev/null; then
    check_step "MongoDB accessible" "mongosh --eval 'db.version()' --quiet"
elif command -v mongo &> /dev/null; then
    check_step "MongoDB accessible" "mongo --eval 'db.version()' --quiet"
else
    echo -e "${YELLOW}⚠ mongosh/mongo CLI not found, checking Docker...${NC}"
    if docker ps | grep -q mongo; then
        echo -e "${GREEN}✓ PASS${NC} MongoDB container is running"
        ((PASSED++))
    else
        echo -e "${RED}✗ FAIL${NC} MongoDB not found"
        ((FAILED++))
    fi
fi
echo ""

# 3. Check .NET Build
echo "=== .NET Build Checks ==="
check_step ".NET SDK installed" "dotnet --version"
echo -n "Building S7.Meridian... "
if dotnet build samples/S7.Meridian > /dev/null 2>&1; then
    echo -e "${GREEN}✓ PASS${NC}"
    ((PASSED++))
else
    echo -e "${RED}✗ FAIL${NC}"
    echo "  Run 'dotnet build samples/S7.Meridian' to see errors"
    ((FAILED++))
fi
echo ""

# 4. Check Configuration Files
echo "=== Configuration Checks ==="
check_step "appsettings.json exists" "test -f samples/S7.Meridian/appsettings.json"

if [ -f samples/S7.Meridian/appsettings.json ]; then
    echo -n "Checking granite3.3:8b in config... "
    if grep -q "granite3.3:8b" samples/S7.Meridian/appsettings.json; then
        echo -e "${GREEN}✓ PASS${NC}"
        ((PASSED++))
    else
        echo -e "${YELLOW}⚠ WARN${NC} Config may be using different model"
    fi
fi
echo ""

# 5. Check Test Data
echo "=== Test Data Checks ==="
check_step "Test document exists" "test -f samples/S7.Meridian/test-data/test-company.txt"
echo ""

# Summary
echo "======================================"
echo "Summary:"
echo -e "  ${GREEN}Passed: $PASSED${NC}"
echo -e "  ${RED}Failed: $FAILED${NC}"
echo "======================================"
echo ""

if [ $FAILED -eq 0 ]; then
    echo -e "${GREEN}✓ All checks passed! System is ready for testing.${NC}"
    echo ""
    echo "Next steps:"
    echo "  1. cd samples/S7.Meridian"
    echo "  2. dotnet run"
    echo "  3. Follow TESTING.md for end-to-end test"
    exit 0
else
    echo -e "${RED}✗ Some checks failed. Please fix the issues above.${NC}"
    echo ""
    echo "Common fixes:"
    echo "  - MongoDB: docker run -d -p 27017:27017 --name meridian-mongo mongo:latest"
    echo "  - Ollama model: ollama pull granite3.3:8b"
    exit 1
fi
