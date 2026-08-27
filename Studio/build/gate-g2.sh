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
echo "=== T0: forbidden remedies ==="
# AGENT_AUTHORED is exported by CI for agent-run jobs; unset here means the
# founder is running it, for whom gate machinery is editable under ADR-0003.
./Studio/build/check-forbidden-remedies.sh

echo
echo "=== T0: write scope ==="
# Reports SKIP until a work order supplies --agent. Becomes blocking in Phase 3.
./Studio/build/check-write-scope.sh ${WORK_ORDER_AGENT:+--agent "$WORK_ORDER_AGENT"}

echo
echo "=== T0: build (warnings are errors) ==="
dotnet build Studio/build/MergeSurvivor.sln --nologo

echo
echo "=== T1: Core unit tests + kernel contract tests ==="
dotnet test Studio/build/MergeSurvivor.sln --nologo --no-build

echo
echo "=== G2 PASSED ==="
