#!/usr/bin/env bash
#
# Refuses the shortcuts that make a build green by making the gate blind.
#
# An autonomous agent under retry pressure will eventually try to delete the
# failing test, weaken the assertion, or edit the workflow that judges it. Those
# are not fixes, and no amount of prompt wording reliably prevents them — so the
# diff is checked mechanically instead.
#
#   ./Studio/build/check-forbidden-remedies.sh [base-ref]
#
# base-ref defaults to origin/main, else HEAD. Comparisons are per-file rather
# than diff-wide: a net count across a large diff is trivially swamped by
# unrelated additions, which would let a weakened assertion slip through.
# Untracked files are inspected too — git diff cannot see them, and a newly
# added workflow is exactly what an agent switching off its own gate looks like.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

BASE="${1:-}"
if [ -z "$BASE" ]; then
  if git rev-parse --verify --quiet origin/main >/dev/null; then
    BASE="origin/main"
  else
    BASE="HEAD"
  fi
fi

if ! git rev-parse --verify --quiet "$BASE" >/dev/null; then
  echo "SKIP: base ref '$BASE' does not exist; nothing to diff against."
  exit 0
fi

MERGE_BASE="$(git merge-base "$BASE" HEAD 2>/dev/null || echo "$BASE")"

TEST_GLOB='Assets/Tests'
status=0

report() {
  echo "FAIL: $1"
  status=1
}

untracked() {
  git ls-files --others --exclude-standard -- "$@" 2>/dev/null || true
}

# --- 1. Test files deleted ----------------------------------------------------
deleted_tests="$(git diff --diff-filter=D --name-only "$MERGE_BASE" -- "$TEST_GLOB" || true)"

if [ -n "$deleted_tests" ]; then
  report "test files were deleted. A failing test is evidence, not an obstacle."
  echo "$deleted_tests" | sed 's/^/       /'
fi

# --- 2. Tests switched off ----------------------------------------------------
# Added lines in tracked files, plus the whole body of any new untracked test file.
ignore_pattern='\[(Ignore|Explicit)(\(|\])|Assert\.Ignore|Assert\.Inconclusive'

ignored="$(git diff "$MERGE_BASE" -- "$TEST_GLOB" \
  | grep -E '^\+' | grep -vE '^\+\+\+' \
  | grep -E "$ignore_pattern" || true)"

for f in $(untracked "$TEST_GLOB"); do
  case "$f" in
    *.cs)
      hit="$(grep -E "$ignore_pattern" "$f" || true)"
      if [ -n "$hit" ]; then
        ignored="${ignored}"$'\n'"${f}: ${hit}"
      fi
      ;;
  esac
done

if [ -n "$ignored" ]; then
  report "a test was marked Ignore/Explicit/Inconclusive. Quarantining a test hides the defect."
  echo "$ignored" | grep -v '^$' | sed 's/^/       /'
fi

# --- 3. Assertions removed, counted per file ----------------------------------
# Per-file so the result does not depend on how far away the base branch is.
assert_pattern='(Assert\.|Assume\.)'

while IFS= read -r f; do
  [ -n "$f" ] || continue
  case "$f" in *.cs) ;; *) continue ;; esac
  # A file that is gone entirely is check 1's business, not this one.
  [ -f "$f" ] || continue

  before="$(git show "$MERGE_BASE:$f" 2>/dev/null | grep -cE "$assert_pattern" || true)"
  after="$(grep -cE "$assert_pattern" "$f" || true)"

  if [ "$after" -lt "$before" ]; then
    report "$f lost $((before - after)) assertion(s). Weakening a check is not a fix."
  fi
done <<< "$(git ls-tree -r --name-only "$MERGE_BASE" -- "$TEST_GLOB" 2>/dev/null || true)"

# --- 4. Gate machinery touched -------------------------------------------------
# Both paths matter equally. The workflow calls gate-g2.sh, so editing that script
# disables the gate just as completely as editing the workflow would — a hole the
# first draft of the permission matrix missed entirely.
# ADR-0003: the founder may change these; a dispatched agent may not. CI exports
# AGENT_AUTHORED=1 for agent-run jobs so one script serves both cases.
gate_changed="$(git diff --name-only "$MERGE_BASE" -- '.github/workflows' 'Studio/build' || true)"
gate_added="$(untracked '.github/workflows' 'Studio/build')"
gate_touched="$(printf '%s\n%s' "$gate_changed" "$gate_added" | grep -v '^$' || true)"

if [ -n "$gate_touched" ]; then
  if [ "${AGENT_AUTHORED:-0}" = "1" ]; then
    report "an agent-authored change touches the gate machinery (.github/workflows or Studio/build). Agents must not be able to switch off the gates that judge them."
    echo "$gate_touched" | sed 's/^/       /'
  else
    echo "NOTE: gate machinery changed. Allowed for the founder under ADR-0003; CODEOWNERS review still applies."
    echo "$gate_touched" | sed 's/^/       /'
  fi
fi

# --- 5. Performance ceilings touched ------------------------------------------
raised="$(git diff "$MERGE_BASE" -- 'Studio' 'Assets' \
  | grep -E '^\+' | grep -vE '^\+\+\+' \
  | grep -iE '(maxAlloc|frameBudget|maxFrameMs|perfThreshold|allocPerFrame)' || true)"

if [ -n "$raised" ]; then
  echo "NOTE: a performance threshold was touched. Thresholds may only move in the strict direction without an ADR:"
  echo "$raised" | sed 's/^/       /'
fi

if [ "$status" -eq 0 ]; then
  echo "OK: no forbidden remedy found (base $MERGE_BASE)"
fi

exit "$status"
