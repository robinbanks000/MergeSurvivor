#!/usr/bin/env bash
#
# Records one gate evaluation as a kernel gate-verdict.
#
#   ./Studio/build/emit-gate-verdict.sh --gate G2 --task WO-0008 \
#       --by ceo-orchestrator --evidence EVD-0010,EVD-0011
#
# Prints the path of the record it wrote.
#
# There is deliberately no --verdict flag. This script RUNS the gate and records
# what happened; it cannot be asked to write "pass". That is the difference
# between a verdict and an assertion, and the reason this path exists at all:
# task.schema.json makes completedByGate a pointer, and until now it pointed at
# nothing, so the first order to reach the gate with its conditions met found the
# last link of the chain missing.
#
# What this does NOT do, stated plainly: it does not prove CI ran. Nothing inside
# a repository can, short of committing from CI, which this studio refuses
# because an unattended actor mutating kernel state is worse than the gap. What
# it does is bind the claim to a commit, so anyone may check that commit out,
# re-run the gate and contradict the record. A false verdict becomes discoverable
# rather than merely improbable.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

GATE=""; TASK=""; BY=""; EVIDENCE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --gate) GATE="$2"; shift 2 ;;
    --task) TASK="$2"; shift 2 ;;
    --by) BY="$2"; shift 2 ;;
    --evidence) EVIDENCE="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

for required in GATE TASK BY EVIDENCE; do
  if [ -z "${!required}" ]; then
    echo "FAIL: --${required,,} is required." >&2
    exit 2
  fi
done

if [ "$GATE" != "G2" ]; then
  echo "FAIL: only G2 can be evaluated by this script; it runs gate-g2.sh." >&2
  echo "      Another gate needs its own evaluator, not a flag on this one." >&2
  exit 2
fi

# A verdict names a commit, so the tree must BE that commit. Evaluating a dirty
# tree would record a result nobody can reproduce, which is the failure this
# record exists to prevent.
if ! git diff --quiet HEAD 2>/dev/null || [ -n "$(git ls-files --others --exclude-standard)" ]; then
  echo "FAIL: the working tree is dirty. A verdict must name the commit it was" >&2
  echo "      evaluated against; commit or stash first." >&2
  exit 2
fi

COMMIT="$(git rev-parse --short=7 HEAD)"
AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

echo "Evaluating $GATE against $COMMIT for $TASK ..." >&2
FAILED_CHECKS=""
if ./Studio/build/gate-g2.sh >/tmp/gate-verdict-$$.log 2>&1; then
  VERDICT="pass"
else
  VERDICT="fail"
  # A failing verdict must name what failed, or the agent has nothing to
  # root-cause -- the schema refuses one that does not.
  FAILED_CHECKS="$(grep -E '^(FAIL|  Failed |=== .* ===)' /tmp/gate-verdict-$$.log | tail -20 || true)"
  if [ -z "$FAILED_CHECKS" ]; then
    FAILED_CHECKS="gate-g2.sh exited non-zero; see the run log"
  fi
fi
rm -f "/tmp/gate-verdict-$$.log"
echo "Verdict: $VERDICT" >&2

# git does not track empty directories, so the verdict directory does not survive
# a clone or a commit that contains no verdict. emit-evidence.sh has always done
# this; I left it out and the very first verdict failed on it after the gate had
# already run and passed.
mkdir -p Studio/state/verdicts

OUT="Studio/state/verdicts/${GATE}-${TASK}-${COMMIT}.json"

python3 - "$OUT" "$GATE" "$TASK" "$VERDICT" "$AT" "$BY" "$EVIDENCE" "$COMMIT" "$FAILED_CHECKS" <<'PY'
import json, sys

out, gate, task, verdict, at, by, evidence, commit, failed = sys.argv[1:10]

record = {
    "kind": "gate-verdict",
    "gate": gate,
    "taskId": task,
    "commit": commit,
    "verdict": verdict,
    "evaluatedAt": at,
    "evaluatedBy": by,
    "evidence": [e.strip() for e in evidence.split(",") if e.strip()],
}

if verdict == "fail":
    record["failedChecks"] = [line.strip() for line in failed.splitlines() if line.strip()]

with open(out, "w") as f:
    json.dump(record, f, indent=2, ensure_ascii=False)
    f.write("\n")
PY

echo "$OUT"
