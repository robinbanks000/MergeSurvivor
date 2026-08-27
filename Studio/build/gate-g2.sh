#!/usr/bin/env bash
#
# The G2 code gate, runnable locally in the same form CI will run it in Phase 2.
# Run this before every push. It needs the .NET SDK and no Unity licence.
#
#   ./Studio/build/gate-g2.sh

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "=== T0: Core purity ==="
./Studio/build/check-core-purity.sh

echo
echo "=== T0: build (warnings are errors) ==="
dotnet build Studio/build/MergeSurvivor.sln --nologo

echo
echo "=== T1: Core unit tests ==="
dotnet test Studio/build/MergeSurvivor.sln --nologo --no-build

echo
echo "=== G2 PASSED ==="
