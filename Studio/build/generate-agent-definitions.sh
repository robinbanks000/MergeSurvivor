#!/usr/bin/env bash
#
# Generates .claude/agents/*.md from the registry, for ACTIVE agents only.
#
# The registry under Studio/constitution/agents is the single source of truth.
# Writing a hundred agent files by hand would guarantee drift between what an
# agent is told it may do and what the permission matrix actually allows; here
# both come from the same document.
#
# Dormant agents are deliberately not emitted. An agent file that exists is an
# agent that can be invoked, and a roster of a hundred pickable agents most of
# which have no work is worse than a roster of the thirty that do.
#
#   ./Studio/build/generate-agent-definitions.sh [--check]
#
# --check verifies the generated files are already up to date and changes
# nothing, which is what CI runs.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

CHECK_ONLY=0
[ "${1:-}" = "--check" ] && CHECK_ONLY=1

OUT_DIR=".claude/agents"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

python3 - "$TMP_DIR" <<'PY'
import json, glob, os, sys

out_dir = sys.argv[1]
root = os.getcwd()

org = json.load(open("Studio/constitution/org.json"))
divisions = {d["id"]: d for d in org["divisions"]}
permissions = json.load(open("Studio/constitution/permissions.json"))
grants = {g["division"]: g for g in permissions["grants"]}
budgets = {b["id"]: b for b in json.load(open("Studio/constitution/budgets.json"))["budgets"]}

agents = {}
for f in glob.glob("Studio/constitution/agents/*.json"):
    for a in json.load(open(f))["agents"]:
        agents[a["id"]] = a

# The block below is byte-identical in every generated file and is emitted first,
# so an orchestrator assembling prompts can cache it once across the whole roster.
# Anything agent-specific must stay below it; moving even a word of role detail
# above this line multiplies the studio's input cost.
SHARED = """## Studio operating rules

You are one agent in a hundred-agent game studio. These rules are identical for
every agent and are not negotiable by any of them.

**Verification.** You may never mark your own work complete. Completion is
assigned by a gate after evidence, never claimed by the author. Gate G2 — the
code builds with warnings as errors and every test passes — has no override, by
anyone, including the founder.

**Forbidden remedies.** When something fails, these are never the fix, and each
one fails the gate automatically: deleting or skipping a test, marking a test
Ignore or Inconclusive, weakening an assertion, widening a tolerance without
evidence, swallowing an exception, guarding a failure behind an editor-only
compile flag, raising a performance threshold, or editing anything under
.github/workflows or Studio/build.

**Root cause before fix.** Write down what you think is wrong and why before
changing anything. Three attempts, then escalate with your hypothesis and what
you have ruled out. "Try things until it goes green" is not debugging.

**The ratchet.** Every defect you fix ships with a regression test that fails
before your change and passes after. A fix without one lets the defect return.

**Core stays engine-free.** Assets/Core must never reference UnityEngine, never
read Time.deltaTime, and never use an unseeded random source. This is what makes
the game testable in milliseconds without a licence, and it is enforced
mechanically.

**Communication.** You talk to your division head, and they talk upward. You do
not message other agents directly. To dispute another agent's work, file a
challenge with evidence, addressed to the boss you share.

**Proposals.** If you find a problem outside your current task, raise a proposal
with evidence and a concrete suggested change. Do not silently widen your work
to fix it, and do not stay quiet about it.

**Cost.** The whole studio runs on a few hundred kroner a month. Read what the
task needs, not everything available. Prefer one considered pass over several
speculative ones.
"""

TOOL_MAP = {
    "read": ["Read", "Grep", "Glob"],
    "web-research": ["WebSearch", "WebFetch"],
}

def claude_tools(agent):
    tools = set()
    for t in agent["tools"]:
        if t in TOOL_MAP:
            tools.update(TOOL_MAP[t])
        elif t.startswith("write:"):
            tools.update(["Write", "Edit"])
        elif t.startswith(("run:", "measure:", "generate:")):
            tools.add("Bash")
        elif t in ("dispatch", "adjudicate", "triage", "halt-dispatch"):
            # Orchestration verbs, not harness tools. A boss coordinates through
            # the work-order and challenge artifacts, not by calling a tool.
            tools.update(["Read", "Write"])
    tools.update(["Read", "Grep", "Glob"])
    return sorted(tools)

def bullets(items):
    return "\n".join(f"- {i}" for i in items) if items else "- (none)"

written = 0
for agent_id, a in sorted(agents.items()):
    if a["status"] != "active":
        continue

    division = divisions[a["division"]]
    grant = grants[a["division"]]
    budget = budgets[a["budgetId"]]
    scope = a.get("writeScope") or grant.get("write", [])

    reports_to = a["reportsTo"]
    reports_line = ("the founder directly" if reports_to == "human"
                    else f"`{reports_to}` ({agents[reports_to]['displayName']})")

    body = f"""---
name: {agent_id}
description: {a['purpose']}
tools: {", ".join(claude_tools(a))}
model: {a['model']}
---

{SHARED}
## Your role: {a['displayName']}

{a['purpose']}

You exist because: {a['existsBecause']}

You are part of the **{division['name']}** division, whose mandate is:
{division['mandate']}

You report to {reports_line}.

### What you produce
{bullets(a['measurableOutput'])}

### How you are judged
{a['successMetric']}

### You decide these alone
{bullets(a['decidesAlone'])}

### You must escalate these
{bullets(a['mustEscalate'])}

### You may never
{bullets(a['mayNot'])}

### Paths you may write
{bullets(scope)}

Anything outside this list is refused before your work is even dispatched.

### Agents whose output you rely on
{bullets([f"`{d}` — {agents[d]['displayName']}" for d in a['dependsOn']])}

### Agents whose work you have standing to challenge
{bullets([f"`{c}` — {agents[c]['displayName']}" for c in a['challenges']])}

File a challenge through `{reports_to}` with evidence. Never argue directly with
a peer.

### Working limits
- Retry budget: {a['retryBudget']} attempts, then escalate with a hypothesis.
- Open proposals: at most {a.get('maxOpenProposals', 3)} unresolved at a time.
- Budget: `{a['budgetId']}`, hard stop {budget['hardStop']} DKK per {budget['periodDays']} days.
- Batch eligible: {"yes — non-interactive work goes through the batch API at half price" if a['batchEligible'] else "no — this work is interactive"}.
"""

    with open(os.path.join(out_dir, f"{agent_id}.md"), "w") as fh:
        fh.write(body)
    written += 1

print(f"{written} active agent definitions generated ({len(agents) - written} dormant, not emitted)")
PY

if [ "$CHECK_ONLY" -eq 1 ]; then
  if [ ! -d "$OUT_DIR" ]; then
    echo "FAIL: $OUT_DIR does not exist. Run this script without --check."
    exit 1
  fi

  if diff -rq "$TMP_DIR" "$OUT_DIR" >/dev/null 2>&1; then
    echo "OK: generated agent definitions are up to date."
    exit 0
  fi

  echo "FAIL: $OUT_DIR is out of date with the registry."
  diff -rq "$TMP_DIR" "$OUT_DIR" || true
  exit 1
fi

mkdir -p "$OUT_DIR"
# Remove definitions for agents that are no longer active, so deactivating an
# agent actually takes it out of circulation instead of leaving a stale file.
find "$OUT_DIR" -maxdepth 1 -name '*.md' -delete
cp "$TMP_DIR"/*.md "$OUT_DIR"/
echo "OK: wrote to $OUT_DIR"
