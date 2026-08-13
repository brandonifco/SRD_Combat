# SRD_Combat Viewer

The Godot client, at its read-only beginning: it **watches** a fight rather than playing
one. The engine resolves a seeded encounter once, forwards, exactly as it would anywhere
else — the viewer keeps a snapshot after every turn and lets you scrub through them. This
is the first slice of Phase 7; the phase ends when a fight can be *played* with a mouse.

## Running it

Needs Godot 4.x with .NET support on `PATH` (`doctor.sh` checks). Nothing else — the
content is committed and found by walking up for `data/srd`, the same way the console
client finds it.

```bash
godot --path client
```

| Key | Does |
| --- | --- |
| Space | play / pause |
| ← / → | step one turn |
| Home / End | first / last turn |
| Esc | quit |

Arguments go after Godot's `--` separator. `--seed=<n>` picks the fight — the same
promise the console client makes, that a seed is a complete bug report:

```bash
godot --path client -- --seed=12345
```

`--capture=<path>` renders one frame to a PNG and quits, with `--at=<turn>` choosing the
turn; it is how a change to this screen gets checked without a person watching it:

```bash
godot --path client -- --capture=/tmp/fight.png --at=14
```

## Why this project is not in SRDCombat.sln

Deliberately. CI runs bare `dotnet restore`, `build` and `test` from the repository root,
which resolve the solution — so every project *in* the solution is built and gated on a
runner that has .NET 8 and no Godot. A client that stays outside the solution cannot
break the gate that protects the engine, and the engine's gate never waits on Godot. The
decision and the trial that proved it are in the plan doc's Phase 7 section and the
Environment section of `CLAUDE.md`.

Two things that arrangement does **not** cost:

- **The build discipline.** `Directory.Build.props` reaches this project anyway — MSBuild
  walks up from the project's own directory — so `TreatWarningsAsErrors`, `Nullable` and
  the analyzers all apply here exactly as they do in `src/`.
- **Building without Godot.** `Godot.NET.Sdk` is a NuGet package, so
  `dotnet build client/SRDCombat.Viewer.csproj` works on a machine that has never seen
  the editor. Only *running* the scene needs Godot itself.

The cost it does have is the honest one: nothing in CI compiles this project, so a
refactor in `src/` can break it silently. Build it before merging anything that touches
the engine's public surface — that is the whole check.

## The rule this client is held to

The same one as the console client: **it holds no rules.** Positions, hit points,
conditions and the narration all come off the engine's public API — `Battlefield`,
`TurnOrder`, `Combatants`, `Log` — and the viewer decides only where to put them on
screen. It recomputes nothing, and the day it needs an engine change is the day the work
has left its lane. The one design decision of its own is in `FightViewer`'s doc comment:
the whole fight is resolved up front into snapshots, because `IRandomSource` is consumed
as the fight goes, and replaying by re-running would be a *different* fight.
