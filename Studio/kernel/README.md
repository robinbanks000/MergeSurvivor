# Kernel contracts

The machine-readable spine of the studio. Every artifact an agent writes or reads has a
schema here, and `MergeSurvivor.Kernel.Tests` validates them inside the G2 gate — so these
are contracts, not documentation.

## The thirteen contracts

| Schema | Governs | The rule it exists to enforce |
|---|---|---|
| `common` | Shared ids, actors, enums | One vocabulary; the agent roster is a closed enum |
| `task` | Work orders and results | A result **cannot** say "completed" — only a gate can |
| `agent` | The roster | `mayDeclareOwnWorkDone` is `const false`; QA cannot edit production code |
| `event` | Append-only studio log | Every state change is reconstructable from one ordered file |
| `message` | Communication | The orchestrator must be sender or recipient — agent-to-agent chat is unrepresentable |
| `memory` | The five memory layers | L0 human-only, L1 append-only, L3 write-once |
| `project-state` | Backlog, sprint, agent status | A working agent must name its task |
| `decision` | ADRs | Irreversible decisions can never be settled by a timeout |
| `evidence` | Verification output | T3 needs metrics and a seed; T5 must come from a human |
| `gate` | Gate registry and verdicts | **G2 has no override** — `overridableBy` is `const []` |
| `permission` | Write access | No grant may name `.github/workflows/` |
| `escalation` | The daily digest | Reversible items need a deadline; irreversible ones must not have one |
| `failure` | Retry and self-correction | No fix without a root-cause hypothesis; a forbidden remedy must escalate |
| `cost` | Budgets and spend | Every budget has a hard stop, and it has teeth |

## Layout

```
schemas/     the contracts
fixtures/
  valid/                 must be accepted
  invalid/               must be REJECTED — each breaks exactly one rule
  cross-check-invalid/   schema-valid, but breaks a rule only the cross-checks can see
kernel-manifest.json     maps every live document to its schema
```

Live documents governed by these schemas are in `Studio/constitution/` (L0),
`Studio/decisions/` (L1), `Studio/state/` (L2) and `Studio/evidence/` (L3).

## Running

```bash
dotnet test Studio/build/MergeSurvivor.Kernel.Tests/MergeSurvivor.Kernel.Tests.csproj
```

Or the whole gate: `./Studio/build/gate-g2.sh`.

## Conventions

- **Fixture naming is load-bearing.** `task.work-order.json` is validated against
  `task.schema.json` — everything before the first dot names the schema.
- **A new kernel document must be added to `kernel-manifest.json`**, or
  `EveryKernelDocumentIsCoveredByTheManifest` fails. Unvalidated state is how registries
  drift apart while each looks fine on its own.
- **The invalid fixtures are the point.** A schema that accepts everything passes every
  positive test while enforcing nothing. If you relax a rule, its rejection fixture starts
  passing validation and the suite goes red — which is the intended alarm, not a nuisance.
- **JSON, not YAML** — see `Studio/decisions/ADR-0002.json`. JSON Schema validates JSON
  natively and agents emit it more reliably than indented YAML. The cost is no comments,
  so explanations live in `description` fields.
