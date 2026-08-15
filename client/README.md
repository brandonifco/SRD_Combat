# SRD_Combat Viewer

The Godot client. **The gauntlet is the default**, exactly as it is in the console: a
run of thirty fights with rests, experience, levelling and loot between them, autosaved
after every cleared fight. The party's turns wait for your mouse, every other side is
taken by the tactics policy, one turn per beat so you can watch what happens to you.
Between fights an interlude reports what the run reports — the rest taken, who returned,
who levelled, what was found — and a Continue button marches on. At each Long Rest a
Shop button opens the merchant's stall: every offer at its printed price, the purse in
the header, the unaffordable dimmed, a click buys, Back or Esc returns. `--one-fight` plays a
single encounter instead; `--watch` keeps the original read-only screen, which resolves
one fight up front and lets you scrub through it.

## Running it

Needs Godot 4.x with .NET support on `PATH` (`doctor.sh` checks, variant included), and
one `dotnet build` before the first launch — Godot does not compile the C# assembly on
its own, and launching without it fails on `Cannot instantiate C# script`, which reads
like a broken checkout rather than the missing step it is. The content needs nothing:
it is committed and found by walking up for `data/srd`, the same way the console client
finds it.

```bash
dotnet build client/SRDCombat.Viewer.csproj -c Debug
godot --path client
```

The run autosaves to `srdcombat-save.json` in the directory Godot was launched from
(`--save=<path>` moves it), and `--continue` resumes it. Defeat does not touch the save —
the file keeps the state after the last fight the party *won*, so reloading is a retry.
`--level=1..5` starts a new run partway up. A save that cannot be read is shown with its
reason and nothing is started, because silently beginning a fresh run would overwrite
the file being asked about.

On your turn:

| Input | Does |
| --- | --- |
| click a square | walk there — the engine charges movement and provokes what it provokes |
| click an enemy | attack with the hardest-hitting attack that reaches, never a bow point blank |
| **arrow keys** | move the cursor around the board — or the highlighted row, while a menu is open |
| **Enter** | take the highlighted menu row, or act on the cursor's square — the same thing a click would do |
| **a letter** | the action whose button shows it: `D` Dodge, `R` Dash, `G` Disengage, `U` Stand Up, `E` Escape, `A` Attack, `C` Cast, `Q` Drink, `P` Give Potion, `W` Second Wind, `S` Action Surge, `F` Rage, `K` Reckless, `M` Steady Aim, `X`/`Z` Cunning Dash/Disengage, `T` Trip, `H`/`J` Spark Heal/Harm |
| **Space** | End Turn |
| Esc | back out of an armed click or open menu; quit when nothing is armed |

**Only what can be used is shown**, and every button carries its key. The row shrinks as
the turn is spent — Dodge and Dash go with the Action, Second Wind with the Bonus Action,
Action Surge appears only once there is no Action left to surge past, Stand Up only while
Prone. The status line above still reads out what is left, so a row that has shrunk says
why. A key is a property of its action rather than of its place in the row, so `D` is
Dodge whenever Dodge is offered and never anything else.

The log is colour-coded: party names blue, monster names orange, and the named thing
being used — a weapon, a spell, a feature, a mastery property — violet, with **damage in
bright red and a miss in yellow**, the two outcomes a reader scans for. A round beginning
and a fight ending stay gold, and a creature dropping is brightened, because those are
the headings of the fight.

**The terms are the fight's own, not a reading of the sentences.** `LogHighlighter`
asks the encounter for its combatants, their attacks, their spells, their stat-block
entries and their features, and colours the text by matching those names; the feature
names come off the `ClassFeature` enum, whose PascalCase is the printed name. Nothing
parses the engine's phrasing, so a reworded narration loses a highlight rather than
gaining a wrong one. Damage and the miss are the two exceptions and are matched as text,
because neither is a name — and they fail the same safe way.

Faint blue squares are where a walk could end; ringed enemies are ones an attack
reaches; a **shaded** square is one this character has Total Cover against, which refuses
an attack, a spell and an area alike. Both are advice, not rules — a click anywhere is sent to the engine, and **a
refusal is shown with its code** rather than swallowed, because a refusal is the engine
explaining a rule. The second row is filtered by what the character *has*, which is
display: a shown button can still be refused, and absent is honest where inert would not
be. A line under the buttons reads out what is left to spend — slots, feature uses,
potions — straight off the engine's state.

Arguments go after Godot's `--` separator. `--seed=<n>` picks the run — the same promise
the console client makes, that a seed is a complete bug report; without one the seed is
fresh, and it is always in the heading. (A `--capture` or `--probe` run falls back to a
fixed seed instead, because a verification image must not change between runs.)
`--create` builds your own party first (Phase 5): every option browsed shows its printed
SRD text and a separate Take commits it, the resolver's word is final on the summary
step, and the four drafts hand off to this screen's ordinary run — save, `--continue`
and defeat-means-reload included. With `--probe` the creation screen drives itself
through the same synthesized clicks, one class of each menu shape, before the play
screen's probe takes over.

```bash
godot --path client -- --seed=12345
```

## Sprite art

The tokens can be animated pixel-art figures instead of circles. Five animations play,
queued in the log's own order so each lands where it belongs: an idle loop while
standing, the walk cycle as the token glides the engine's recorded path, a swing for
every attack (Opportunity Attacks included, faced at the target), a flinch as damage
lands, and the body going down when a creature drops — settling into a corpse, dimmed
for the dead and ringed for the still-saveable. The party faces right and the monsters
left (the columns the factory places), and a walker faces the way it is going.

**Every animation of one character is drawn at one size, through one transform.** The
packs turn out to be canvas-aligned — across every strip the game draws, the figure's
feet rest on the canvas's bottom edge — so a character is measured once, from the
strips in which it is standing, and every strip is then drawn through that. What
differs between strips is motion the artist drew: a Knight's swing lunges twenty pixels
forward, a walk cycle strides and bobs, a slain goblin sprawls sideways. Measuring each
strip on its own and re-centring it, which is what this did at first, deletes that
motion and makes the figure change size mid-swing, because an extended sword widens the
box that the body is scaled to fit.

The board shares one pixel scale, set so a standing human fills its square, and only a
creature too big for a square (the dragon, half as tall as it is wide) is cut down to
fit. That keeps every pack at the same pixel size — they are drawn at the same
resolution — so a goblin reads shorter than an orc because the artist drew it shorter.
A death animation settles on the last frame that is still a body, not the final one:
every pack ends by sinking or fading the corpse away, and holding that frame left a
killed goblin as a smear on the floor. The art is the free Craftpix character packs, and the maps
are curated in `SpriteLibrary`: party art by class name, monster art by **exact**
stat-block name, and anything unmapped keeps the circle-and-letter token — a red
dragon sprite on a Green Dragon Wyrmling would be the display lying, so only the
colours the packs actually hold are mapped.

**The PNGs are deliberately not in the repository.** Craftpix's free license permits
using the art in a game but not redistributing the assets, and this repo is public —
the same line the SRD PDF sits behind. To light the sprites up, download the packs
from [craftpix.net](https://craftpix.net/freebies/) (the free 2D character sets:
Knight, Gladiator, Elf, Priest, Goblin, Orc, Skeleton, Zombie, Dragon, and the three
mages) and unpack them so each character's folder of animation strips sits at:

```
client/assets/sprites/<Character>/Idle.png (Walk.png, Attack.png, Hurt.png, Dead.png)
```

`Idle.png` is the only one a character cannot do without — it is what a standing token
shows, and what the figure is measured from. Everything else degrades on its own: no
`Attack.png` and that creature never swings, no `Hurt.png` and it never flinches, no
`Dead.png` (the Priest packs ship neither) and it lies its idle frame on its back.

The directory is gitignored. A machine without it — CI, a fresh clone — draws the
circle tokens it always drew; every lookup is a fallback, nothing is load-bearing.
`--probe` and `--capture` freeze the animation clock for the same reason they skip
the walk hop: a verification image must not depend on when the frame was taken.

An armed area spell may be aimed at a *bare square* as well as a creature — click the
spell, then click the ground where it should erupt; the engine's point overload rules
on range, shape and who the area catches. A spell with no area still needs a creature,
and the refusal says so.

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

The play screen's verification loop: it drives the screen through the real input path —
synthesized clicks through the viewport, not calls around the input layer — and captures
a PNG after each step: the run's opening interlude, then commanded turns (a refusal on
purpose — Stand Up while not Prone — a walk toward the nearest enemy, an attack, a
feature, a caster's menu-choice-target flow), then plays the fight out and captures the
other side of it: the post-fight interlude with its save, or the defeat screen. Seed 1
clears fight 1 that way; the default seed loses it — both ends of `HandleFightEnd` have
been watched.

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
