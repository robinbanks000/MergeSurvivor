#!/usr/bin/env bash
#
# Turns a verification run into a kernel evidence record.
#
# This is what closes the loop between Phase 1 and Phase 2: without it a CI run
# leaves its result in a log that nothing reads, and gates would have to take an
# agent's word for what happened. With it, CI output becomes kernel state that
# the existing contract tests validate.
#
#   ./Studio/build/emit-evidence.sh --tier T1 --verdict pass \
#       --summary "Core suite green" [--metrics '{"testsPassed":45}'] \
#       [--seed 12345] [--task WO-0142] [--dir tests] [--by ci]
#
# Prints the path of the record it wrote.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

TIER=""; VERDICT=""; SUMMARY=""; METRICS=""; SEED=""; TASK=""; SUBDIR="tests"; BY="ci"

while [ $# -gt 0 ]; do
  case "$1" in
    --tier) TIER="$2"; shift 2 ;;
    --verdict) VERDICT="$2"; shift 2 ;;
    --summary) SUMMARY="$2"; shift 2 ;;
    --metrics) METRICS="$2"; shift 2 ;;
    --seed) SEED="$2"; shift 2 ;;
    --task) TASK="$2"; shift 2 ;;
    --dir) SUBDIR="$2"; shift 2 ;;
    --by) BY="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

for required in TIER VERDICT SUMMARY; do
  if [ -z "${!required}" ]; then
    echo "FAIL: --${required,,} is required." >&2
    exit 2
  fi
done

mkdir -p "Studio/evidence/$SUBDIR"

# Next free id across the whole evidence tree, so two subdirectories cannot
# collide on a number.
NEXT="$(python3 -c "
import glob, re
ids = [int(m.group(1)) for f in glob.glob('Studio/evidence/**/EVD-*.json', recursive=True)
       for m in [re.search(r'EVD-(\d+)\.json$', f)] if m]
print(max(ids) + 1 if ids else 1)
")"
EVD_ID="$(printf 'EVD-%04d' "$NEXT")"
OUT="Studio/evidence/$SUBDIR/$EVD_ID.json"

COMMIT="$(git rev-parse --short=7 HEAD 2>/dev/null || echo "")"
AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

python3 - "$OUT" "$EVD_ID" "$TIER" "$BY" "$AT" "$VERDICT" "$SUMMARY" "$COMMIT" "$METRICS" "$SEED" "$TASK" <<'PY'
import json, sys

out, evd_id, tier, by, at, verdict, summary, commit, metrics, seed, task = sys.argv[1:12]

record = {
    "id": evd_id,
    "tier": tier,
    "producedBy": by,
    "at": at,
    "verdict": verdict,
    "summary": summary,
}

# The schema requires a commit for every automated tier: evidence that cannot be
# tied to a state of the code cannot be reasoned about later.
if tier in ("T0", "T1", "T2", "T3", "T4") and commit:
    record["commit"] = commit
if task:
    record["taskId"] = task
if seed:
    record["seed"] = int(seed)
if metrics:
    record["metrics"] = json.loads(metrics)

with open(out, "w") as f:
    json.dump(record, f, indent=2, ensure_ascii=False)
    f.write("\n")
PY

echo "$OUT"
