#!/usr/bin/env bash
#
# Asserts that a change stays inside the write scope its agent was granted.
#
#   ./Studio/build/check-write-scope.sh --agent gameplay-engineer [base-ref]
#
# Without --agent the check reports SKIP and exits 0. That is a precondition,
# not a disabled alarm: agent attribution does not exist until Phase 3 creates
# the agents, and a check that invents an answer would be worse than one that
# says plainly it has nothing to check.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

AGENT=""
BASE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --agent) AGENT="${2:-}"; shift 2 ;;
    *) BASE="$1"; shift ;;
  esac
done

if [ -z "$AGENT" ]; then
  echo "SKIP: no --agent given, so there is no grant to check this diff against."
  echo "      Becomes blocking in Phase 3, when work orders carry an agent id."
  exit 0
fi

if [ -z "$BASE" ]; then
  if git rev-parse --verify --quiet origin/main >/dev/null; then
    BASE="origin/main"
  else
    BASE="HEAD"
  fi
fi

PERMISSIONS="Studio/constitution/permissions.json"
MERGE_BASE="$(git merge-base "$BASE" HEAD 2>/dev/null || echo "$BASE")"

# Grant = write ∪ append. Read straight from the constitution so this can never
# drift from what the orchestrator enforces at dispatch time.
# Grants are keyed by division, not by agent: a hundred individual grants would
# drift from the roster within weeks. The agent's division comes from the
# registry, so the two documents cannot disagree about who may write what.
mapfile -t ALLOWED < <(python3 -c "
import json, glob, sys
agent = sys.argv[1]

division = None
for f in glob.glob('Studio/constitution/agents/*.json'):
    for a in json.load(open(f))['agents']:
        if a['id'] == agent:
            division = a['division']
            break
if division is None:
    sys.exit(0)

data = json.load(open('$PERMISSIONS'))
grants = [g for g in data['grants'] if g['division'] == division]
if not grants:
    sys.exit(0)

g = grants[0]
for p in g.get('write', []) + g.get('append', []):
    print(p)
" "$AGENT")

# mapfile reports its own exit status, not the substituted process's, so an
# unknown agent has to be detected here. Without this it would fall through to
# "everything is out of scope" — the right verdict for the wrong reason, and a
# misleading one to debug at three in the morning.
if [ "${#ALLOWED[@]}" -eq 0 ]; then
  echo "FAIL: '$AGENT' has no grant in $PERMISSIONS. Every agent must be listed there before it can be dispatched."
  exit 1
fi

mapfile -t EXCLUSIVE < <(python3 -c "
import json
print('\n'.join(json.load(open('$PERMISSIONS'))['humanExclusivePaths']))
")

# Changed files, tracked and untracked alike.
changed="$( { git diff --name-only "$MERGE_BASE"; git ls-files --others --exclude-standard; } | sort -u | grep -v '^$' || true)"

if [ -z "$changed" ]; then
  echo "OK: no changes to check."
  exit 0
fi

matches_glob() {
  # "Assets/Core/**" matches "Assets/Core/Merge/X.cs"; compared on a path-segment
  # boundary so "Assets/Core" never looks like a prefix of "Assets/CoreUtils".
  local path="$1" glob="$2" prefix
  prefix="${glob%\*\*}"
  prefix="${prefix%/}"
  [ "$path" = "$prefix" ] && return 0
  case "$path" in "$prefix"/*) return 0 ;; esac
  return 1
}

status=0
for file in $changed; do
  reserved=""
  for ex in "${EXCLUSIVE[@]}"; do
    if matches_glob "$file" "$ex"; then reserved="$ex"; break; fi
  done

  if [ -n "$reserved" ]; then
    echo "FAIL: $file is human-exclusive ($reserved) and '$AGENT' changed it."
    status=1
    continue
  fi

  ok=1
  for allow in "${ALLOWED[@]}"; do
    if matches_glob "$file" "$allow"; then ok=0; break; fi
  done

  if [ "$ok" -ne 0 ]; then
    echo "FAIL: $file is outside the write scope granted to '$AGENT'."
    status=1
  fi
done

if [ "$status" -eq 0 ]; then
  echo "OK: every change is inside the grant for '$AGENT'."
fi

exit "$status"
