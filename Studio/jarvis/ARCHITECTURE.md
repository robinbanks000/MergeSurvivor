# JARVIS

The studio's operating layer. It is not a game, it is not part of one, and no game
imports it.

This file exists so that a session with no memory of how any of this was built can pick
it up from the repository alone. Nothing important about JARVIS lives in a chat log.

## What JARVIS is

A read-only cockpit over the studio's own committed state, plus the kernel records and
checks that make that state trustworthy.

    python3 Studio/jarvis/build-jarvis.py           # regenerate the page
    python3 Studio/jarvis/build-jarvis.py --check   # regenerate and validate it

The page is `Studio/jarvis/jarvis.html`. It is **gitignored on purpose**: it is derived
entirely from the records under `Studio/`, and a committed copy would go stale the moment
anything was filed. A stale cockpit is worse than no cockpit. Regenerate it; never commit
it.

## Three rules the tool is built on

1. **It reads; it never writes.** Nothing in `build-jarvis.py` can change a work order's
   status, sign a gate or file a record. Regenerating the page cannot alter one byte of
   studio state.
2. **It never renders a status it cannot verify.** Anything it cannot establish from disk
   is `UNKNOWN` (no record exists) or `UNAVAILABLE` (a source exists, a checkout cannot
   reach it). These are different claims and collapsing them is a regression the
   validator refuses.
3. **It is separate from the games.** Standard library only, no npm, no lockfile. It
   imports nothing from `Assets/`, and nothing under `Assets/` may mention it — enforced
   by `NoProjectFileReachesIntoTheStudioLayer`.

## Two vocabularies on the page

- **Claim states** — how far a claim reaches: `REAL`, `PENDING`, `UNAVAILABLE`, `UNKNOWN`.
- **Claim classes** — what kind of statement it is: `FACT`, `OBSERVATION`, `INFERENCE`,
  `RECOMMENDATION`, `PROPOSAL`.

The second exists because a dashboard that mixes "14 evidence records exist" with "you
should wire the validator into G2" without marking which is which lends the authority of
a measurement to an opinion. `check()` fails the build if nothing on the page is marked
`FACT` or `RECOMMENDATION`.

## Where state lives

| What | Where | Governed by |
|---|---|---|
| Org chart, divisions | `Studio/constitution/org.json` | `org.schema.json` |
| Agent roster (100 records) | `Studio/constitution/agents/*.json` | `agent.schema.json` |
| Projects and boundaries | `Studio/constitution/projects.json` | `project.schema.json` |
| Permissions, budgets, gates, memory | `Studio/constitution/` | one schema each |
| Current phase, backlog, founder queue | `Studio/state/project-state.json` | `project-state.schema.json` |
| Capability gaps | `Studio/state/gaps/GAP-*.json` | `capability-gap.schema.json` |
| Proposals, challenges, rulings, escalations, events, verdicts, reports | `Studio/state/` | one schema each |
| Architecture decisions | `Studio/decisions/ADR-*.json` | `decision.schema.json` |
| Evidence | `Studio/evidence/**/EVD-*.json` | `evidence.schema.json` |
| Work orders | `Studio/orders/**/WO-*.json` | `task.schema.json` |

Every one of these is listed in `Studio/kernel/kernel-manifest.json`. A kernel document
that is not in the manifest fails `EveryKernelDocumentIsCoveredByTheManifest` — an
unvalidated state file is a test failure, not a convenience.

**Only git-tracked files count as studio memory.** JARVIS filters everything it reads
through `git ls-files`. An untracked record is invisible to the page and to the gate
alike, which is why a newly written record must be staged before it exists as far as the
studio is concerned.

## The workforce

100 agent records in 13 divisions: 30 active, 70 dormant, 0 retired.

**100 is not a target and not a ceiling** (ADR-0005). The roster may grow past it to close
a filed capability gap, or shrink below it as roles retire. What replaced the count is a
sequence:

    capability gap filed with evidence
      → existing roster examined agent by agent and found unable to absorb the work
      → activation of a dormant specialist ruled out explicitly (always the cheaper answer)
      → specialist proposed inside the gap record
      → checked against the live roster for duplicate outputs while still a proposal
      → creation performed only by a human edit under Studio/constitution/

Anti-sprawl is mechanical, not numerical. Duplicate measurable outputs, near-duplicate
outputs (Jaccard ≥ 0.8), duplicate success metrics, unresolvable reporting lines, agents
that can only read, and expensive models outside four named roles all fail the build.

**Retirement is a status change, never a deletion.** Other agents' `dependsOn` and
`challenges` lists still name a retired id, and `generate-agent-definitions.sh` resolves
display names from the whole registry — deleting a record breaks both. A retired agent
must state `retiredBecause` and `retiredAt`, and is excluded from the distinctness checks
so its measurable output does not block a successor forever.

## Authority

`Studio/constitution/**`, `Studio/kernel/schemas/**`, `Studio/build/**`,
`.github/workflows/**`, `ProjectSettings/**` and `Assets/Monetization/**` are
**human-exclusive**. No agent writes them at any tier, which is why every workforce
change needs the founder however much evidence a gap record carries.

Gate G2 has no override, by anyone, including the founder.

The loop is OBSERVE → ANALYZE → PROPOSE → VALIDATE → AUTHORIZE → EXECUTE. Work already
authorised as normal operation proceeds without asking. Anything touching authority,
security, the constitution or destructive permissions stops at AUTHORIZE, and the stop is
enforced by the path list rather than by an agent's good behaviour.

Filing a record never grants authority. A capability gap is evidence someone would need
before asking; it is not permission.

## Projects

JARVIS is the studio layer. Each project is a separate product in its **own repository**,
referred to rather than contained.

    robinbanks000/JARVIS          the studio layer
      └── mergesurvivor           robinbanks000/MergeSurvivor @ main
                                  owns Assets/**, Packages/**, ProjectSettings/**
                                  — in ITS repository, never in this one

`Studio/constitution/projects.json` is the integration boundary. A project entry carries
`repo` (owner/name) and `ref` (the branch JARVIS treats as its current state); `owns`
describes what that project holds **in its own repository**. Adding a second project means
appending an entry — not restructuring anything above it.

Two checks defend the boundary in both directions:

- `NoProjectClaimsAPathInsideTheStudioLayer` — no project may claim a studio path.
- `NoProjectOwnedPathExistsInTheStudioRepository` — no file belonging to an externally
  hosted project may exist here. Absence is the whole property: put `Assets/` back into
  this repository and the build fails.

The second check compares each project's `repo` against the repository the checkout came
from, so it enforces only where enforcement means something. A project hosted in the same
repository as the studio layer is exempt and the test says so in its output — which is
how it behaved before the separation, and why it did not fail on a combined repository
that had done nothing wrong.

## Verifying a change

    ./Studio/build/gate-g2.sh                        # the full gate; needs the .NET SDK
    python3 Studio/jarvis/build-jarvis.py --check    # the cockpit's own validator

`gate-g2.sh` runs Core purity, forbidden remedies, write scope, agent-definition
freshness, then the build and the C# test suites — `KernelContractTests` (schemas and
manifest), `KernelCrossCheckTests` (record integrity), `OrgCrossCheckTests` (the roster
and hierarchy) and `WorkforceCrossCheckTests` (capability gaps and project boundaries).

The cockpit's `--check` validates the generated page: every section reachable from the
navigation, every status chip inside the vocabulary, every table cell labelled and
wrapped, `UNKNOWN` and `UNAVAILABLE` both still rendered somewhere, and the document
shell intact.

## Known limitation

`build-jarvis.py --check` is **not wired into any gate**. The page can start
misrepresenting the studio between one commit and the next with nothing objecting. This
is recorded as the concrete cost of `GAP-0001`, which is open: nobody in the roster owns
the studio's own operating tools.
