#!/bin/bash
# run_tests.sh — Build and run all FNA_RTS tests in headless mode.
# Usage: ./run_tests.sh
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
FNA_DIR="$SCRIPT_DIR/../FNA"
FNA3D_BUILD="$FNA_DIR/lib/FNA3D/build"
PASS=0
FAIL=0

# ─── Pre-build FNA3D if needed ────────────────────────────────────────
if [ ! -f "$FNA3D_BUILD/libFNA3D.so.27.0.0" ]; then
    echo "=== Building FNA3D ==="
    ninja -C "$FNA3D_BUILD" 2>&1 | tail -1
fi

# ─── Step 1: FNARTS.Core.Tests (xUnit, no GPU) ────────────────────────
echo "=== FNARTS.Core.Tests ==="
CORE_TEST_OUT=$(dotnet test "$SCRIPT_DIR/tests/FNARTS.Core.Tests/FNARTS.Core.Tests.csproj" \
    --nologo --verbosity quiet 2>&1)
if echo "$CORE_TEST_OUT" | grep -q "Failed:.*0"; then
    echo "  => PASS"
    PASS=$((PASS + 1))
else
    echo "  => FAIL"
    echo "$CORE_TEST_OUT" | tail -3
    FAIL=$((FAIL + 1))
fi

# ─── Step 2: FNARTS.Game.Tests (headless FNA, needs GPU) ──────────────
echo "=== FNARTS.Game.Tests ==="
GAME_TEST_OUT="$SCRIPT_DIR/tests/FNARTS.Game.Tests/bin/Debug/net10.0"
dotnet build "$SCRIPT_DIR/tests/FNARTS.Game.Tests/FNARTS.Game.Tests.csproj" \
    --nologo -clp:NoSummary 2>&1 | tail -1
ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$GAME_TEST_OUT/libFNA3D.so"
ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$GAME_TEST_OUT/libFNA3D.so.0"
if dotnet run --no-build --project \
    "$SCRIPT_DIR/tests/FNARTS.Game.Tests/FNARTS.Game.Tests.csproj" -- \
    --headless 2>&1 | grep -q "RESULT:.*PASS"; then
    echo "  => PASS"
    PASS=$((PASS + 1))
else
    echo "  => FAIL"
    FAIL=$((FAIL + 1))
fi

# ─── Step 3: FNARTS.Game headless smoke test ──────────────────────────
echo "=== FNARTS.Game (headless smoke) ==="
GAME_OUT="$SCRIPT_DIR/src/FNARTS.Game/bin/Debug/net10.0"
dotnet build "$SCRIPT_DIR/src/FNARTS.Game/FNARTS.Game.csproj" \
    --nologo -clp:NoSummary 2>&1 | tail -1
ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$GAME_OUT/libFNA3D.so"
ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$GAME_OUT/libFNA3D.so.0"
if dotnet run --no-build --project \
    "$SCRIPT_DIR/src/FNARTS.Game/FNARTS.Game.csproj" -- \
    --headless 2>&1 | grep -q "RESULT:.*PASS"; then
    echo "  => PASS"
    PASS=$((PASS + 1))
else
    echo "  => FAIL"
    FAIL=$((FAIL + 1))
fi

# ─── Summary ──────────────────────────────────────────────────────────
echo ""
echo "========================================"
echo "  FNA_RTS Results: $PASS passed, $FAIL failed"
echo "========================================"

if [ "$FAIL" -gt 0 ]; then
    exit 1
fi
exit 0
