# MergeSurvivor

A Unity merge/survivor game, built by an autonomous multi-agent studio.
The autonomous studio that builds it lives in its own repository:
[robinbanks000/JARVIS](https://github.com/robinbanks000/JARVIS). This repository is the
game and nothing else.

## Layout

| Path | What it is |
|---|---|
| `Assets/Core/` | The game. Plain C#, **no UnityEngine**. Deterministic and seeded. |
| `Assets/Unity/` | The shell. MonoBehaviours that read input and draw things. |
| `Assets/Tests/Core.Tests/` | Core tests. Run by `dotnet test` **and** the Unity Test Framework. |
| `Tools/` | dotnet projects + gate scripts, so CI can verify Core with no Unity licence. |
| `Sim/` | headless simulation harness over Core, used by the balance gate. |

The split is the point: gameplay rules live in `Core` where they can be tested in
milliseconds and simulated thousands of times, and `Unity` stays thin enough that
very little logic is trapped behind the editor.

## Running the checks

```bash
./Tools/gate-g2.sh
```

That is the whole G2 code gate — Core purity, a warnings-as-errors build, and the
unit tests. It needs the .NET SDK and nothing else. Run it before every push.

To run only part of it:

```bash
./Tools/check-core-purity.sh                     # is Core still engine-free?
dotnet test Tools/MergeSurvivor.sln              # just the tests
```

## Opening in Unity

The project targets the Unity version pinned in
`ProjectSettings/ProjectVersion.txt` (currently `6000.0.32f1`). If you have a
different Unity 6 LTS installed, change that file to match before opening —
otherwise Unity Hub will offer to upgrade the project.

On first open Unity generates `Library/`, the remaining `ProjectSettings/` assets
and all `.meta` files. Those are expected; only `Library/` is gitignored.

There is no scene yet. The MonoBehaviours in `Assets/Unity/` are adapters waiting
to be wired to prefabs — `PlayerController`, `EnemySpawner` and `SimplePool` all
take their references through the inspector.

## Conventions worth knowing

- **Nothing in `Assets/Core` may reference Unity.** Enforced twice: by
  `"noEngineReferences": true` in the Core asmdef, and by `check-core-purity.sh`
  in CI.
- **Core takes `dt` as a parameter**, never reads `Time.deltaTime`, so a
  simulation can step faster than real time.
- **All randomness goes through `IRng`.** `UnityEngine.Random` is global mutable
  state and cannot be replayed; a seeded `XorShiftRng` can.
- **Tests use the NUnit constraint model** (`Assert.That(x, Is.EqualTo(y))`),
  which behaves identically under Unity's NUnit 3 and standalone NUnit.
