# SRD_Combat Viewer

The Godot client. **Playing is the default**: the party's turns wait for your mouse,
every other side is taken by the tactics policy, one turn per beat so you can watch what
happens to you. `--watch` keeps the original read-only screen, which resolves the whole
fight up front and lets you scrub through it. The phase this client belongs to ends when
a fight can be played with a mouse; the core of that — move, attack, the basic actions,
end turn — is here, and spells, class features and potions are the slices that remain.

## Running it

Needs Godot 4.x with .NET support on `PATH` (`doctor.sh` checks, variant included).
Nothing else — the content is committed and found by walking up for `data/srd`, the same
way the console client finds it.

```bash
godot --path client
```

On your turn:

| Input | Does |
| --- | --- |
| click a square | walk there — the engine charges movement and provokes what it provokes |
| click an enemy | attack with the hardest-hitting attack that reaches |
| Dodge / Dash / Disengage / Stand Up / Escape | the untargeted actions, as buttons |
| End Turn | pass |
| Esc | quit |

Faint blue squares are where a walk could end; ringed enemies are ones an attack
reaches. Both are advice, not rules — a click anywhere is sent to the engine, and **a
refusal is shown with its code** rather than swallowed, because a refusal is the engine
explaining a rule.

Arguments go after Godot's `--` separator. `--seed=<n>` picks the fight — the same
promise the console client makes, that a seed is a complete bug report:

```bash
godot --path client -- --seed=12345
```

### The read-only screen

```bash
godot --path client -- --watch
```

Space plays/pauses, ←/→ step one turn, Home/End jump, Esc quits. `--capture=<path>`
renders one frame to a PNG and quits (with `--at=<turn>` choosing the turn), and implies
`--watch` — a capture of a fight nobody is playing is the watch screen's job.

### The probe

```bash
godot --path client -- --probe=<directory>
```

The play screen's verification loop: it drives one commanded turn through the real input
path — synthesized clicks through the viewport, not calls around the input layer — and
captures a PNG after each step: turn ready, a refusal on purpose (Stand Up while not
Prone), a walk toward the nearest enemy, an attack, the turn ended. It is how a change
to this screen gets checked without a person clicking.

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
conditions and the narration all come off the engine's public API, every action is one
of the engine's own and every refusal is displayed, never interpreted. The one choice
the client makes — which attack a click means — is a player convenience, not a rule, and
it is shared with the console client (`AttackChoice` in `SRDCombat.Game`) so the two
cannot drift apart on it. Even the movement highlight is the engine's own
`MovementRules.FindPath`, asked once per square; the play screen decides only what to
colour.

The screens split over one design fact, written on `WatchMode`: `IRandomSource` is
consumed as a fight goes, so scrubbing means resolving once and snapshotting every turn,
while playing means holding the one live `Encounter` and never replaying anything.
