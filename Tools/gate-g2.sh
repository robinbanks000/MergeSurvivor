#!/usr/bin/env bash
#
# The G2 code gate for MergeSurvivor, runnable locally in the same form CI runs it.
# It needs the .NET SDK and no Unity licence.
#
#   ./Tools/gate-g2.sh
#
# This is the game's half of the gate that used to live at Studio/build/gate-g2.sh.
# The other half -- the kernel contract, cross-check, org and workforce tests --
# went with the studio layer when JARVIS became its own repository, because those
# tests read Studio/ records and never touched a single game file.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "=== T0: Core purity ==="
./Tools/check-core-purity.sh

echo
echo "=== T0: build (warnings are errors) ==="
dotnet build Tools/MergeSurvivor.sln --nologo

echo
echo "=== T1: Core unit tests ==="
dotnet test Tools/MergeSurvivor.sln --nologo --no-build

echo
echo "=== G2 PASSED ==="
