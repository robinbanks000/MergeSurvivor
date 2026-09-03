#!/usr/bin/env bash
#
# Enforces the architecture's central rule: Assets/Core must not depend on Unity.
#
# Unity itself already enforces this through "noEngineReferences": true in the Core
# asmdef, but that check only runs inside the editor. This script gives the same
# guarantee to the G2 code gate, which runs with no Unity licence at all.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CORE_DIR="$ROOT/Assets/Core"
CORE_ASMDEF="$CORE_DIR/MergeSurvivor.Core.asmdef"

status=0

if [ ! -d "$CORE_DIR" ]; then
  echo "FAIL: $CORE_DIR does not exist."
  exit 1
fi

# 1. No Unity types anywhere in Core sources.
#
# Comments are stripped first. Prose that merely names a Unity type — such as the
# note in IRng.cs explaining why UnityEngine.Random is banned — is documentation,
# not a dependency, and failing the gate on it would train agents to stop writing
# the explanations. sed leaves the lines in place so line numbers stay accurate.
# Only // comments are handled, which is all this codebase uses.
violations=""
while IFS= read -r -d '' file; do
  hits="$(sed -E 's://.*::' "$file" \
    | grep -nE '(^|[^A-Za-z0-9_])(UnityEngine|UnityEditor)([^A-Za-z0-9_]|$)' || true)"

  if [ -n "$hits" ]; then
    while IFS= read -r hit; do
      violations+="${file#"$ROOT"/}:${hit}"$'\n'
    done <<< "$hits"
  fi
done < <(find "$CORE_DIR" -name '*.cs' -print0)

if [ -n "$violations" ]; then
  echo "FAIL: Assets/Core references Unity. Core must stay engine-free so it can be"
  echo "      tested and simulated without a licence. Move this code to Assets/Unity."
  echo "$violations"
  status=1
fi

# 2. The asmdef guard must stay switched on, or the editor would stop enforcing it.
if ! grep -q '"noEngineReferences": true' "$CORE_ASMDEF"; then
  echo "FAIL: $CORE_ASMDEF no longer sets \"noEngineReferences\": true."
  status=1
fi

if [ "$status" -eq 0 ]; then
  echo "OK: Assets/Core is engine-free."
fi

exit "$status"
