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
# Reports SKIP until a work order supplies --agent.
#
# The base ref matters and defaults differently by caller. In CI the question is
# "what does this whole branch change against main", so the script's own default
# is right. For a single agent's step the question is "what did this agent just
# change", and diffing against main would flag every file the branch has touched
# since it forked — 146 of them on this branch, none of them the agent's. So a
# per-agent check defaults to HEAD unless the caller says otherwise.
./Studio/build/check-write-scope.sh \
  ${WORK_ORDER_AGENT:+--agent "$WORK_ORDER_AGENT"} \
  ${WORK_ORDER_AGENT:+${WORK_ORDER_BASE:-HEAD}}

echo
echo "=== T0: agent definitions match the registry ==="
# The registry is the source of truth; a hand-edited agent file would tell an
# agent it may do something the permission matrix refuses.
./Studio/build/generate-agent-definitions.sh --check

echo
echo "=== T0: build (warnings are errors) ==="
dotnet build Studio/build/MergeSurvivor.sln --nologo

echo
echo "=== T1: Core unit tests + kernel contract tests ==="
dotnet test Studio/build/MergeSurvivor.sln --nologo --no-build

echo
echo "=== G2 PASSED ==="
