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
| **Tab** | cycle the valid targets of whatever is armed; with nothing armed, arm the attack and start cycling — Enter then swings the best weapon at the cursor's target |
| **a letter** | the action whose button shows it: `D` Dodge, `R` Dash, `G` Disengage, `U` Stand Up, `E` Escape, `A` Attack, `C` Cast, `Q` Drink, `P` Give Potion, `W` Second Wind, `S` Action Surge, `F` Rage, `K` Reckless, `M` Steady Aim, `X`/`Z` Cunning Dash/Disengage, `T` Trip, `H`/`J` Spark Heal/Harm |
| **Space** | End Turn |
| **mouse wheel** | zoom the camera, about the pointer |
| **middle- or right-drag** | pan the camera |
| Esc | back out of an armed click or open menu; with nothing armed, ask to quit — Esc again quits, anything else stays |

**The chrome anchors to the window's real edges, whatever they are.** The panel keeps
the right edge, the banner and buttons keep the bottom, and the camera composes the
fight into whatever ground is left — so the controls are visible at every window size
and resolution, and the window refuses to shrink below 960×540, where there would be no
ground left to give. (They were laid out on a fixed 1920×1080 canvas once, and on any
screen shorter than that the button row sat below the window's bottom edge, invisible
no matter how the window was sized.)

**The field fills the window, and a camera frames the fight over it.** Everything else
— the heading, the initiative list, the log, the banner and the buttons — floats on
translucent panels with the ground running underneath. The camera zooms to hold every
living combatant with some ground around them, leaning toward whoever is acting: it
zooms in as the fight clumps and back out as it spreads, scrolling with the action
rather than showing the whole board at once. The terrain is drawn to the window's
edges wherever the camera sits — ground beyond the playable field simply continues,
unwashed (a darkening wash was tried and disliked from play), so the window is one
unbroken battlefield; the boundary reads from the movement highlight and the cursor,
and rule washes and scenery still never draw out there. The
wheel and a middle- or right-drag take the camera by hand; the fight takes it back the moment
the next animation or turn starts. Zooming the wheel all the way out shows the whole
field in its surroundings.

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
reaches; the **fog of war** shadows every square no party member can see (`PartyVision`
in `Game`: a wall blocks the line, sight is the whole party's union, and Unconscious or
Blinded eyes count for nothing), drawn smooth so its edge feathers rather than steps. A
monster standing in the fog is invisible — no token, no ring, no hover hint, no Tab
stop, and the initiative panel shows its row as `unseen` — until someone's line to it
clears. All of it is advice, not rules — a click anywhere is sent to the engine, and **a
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
left (the columns the factory places), and a walker faces the way it is going. The walk
cycle advances with the *distance covered* rather than with a timer, so the legs can
never skate: change how fast a token crosses the ground and the stride follows.

**One clock runs the board, at ten frames a second**
(`FightScreen.AnimationFramesPerSecond`), and it is the only number to change to
re-pace the whole screen. Idle, walk, swing, flinch and fall all advance at that rate;
a pose therefore lasts as long as its own frames take, so a five-frame Goblin swing is
half a second and a fourteen-frame Priest attack is a second and a half. Even how fast
the ground goes by comes off it — a square costs the paces that cover it, two fifths of
a second, so a thirty-foot move is about two and a half. Each of those used to be its
own number: idle ticked at eight a second, a walk cycle at twenty, and a pose was
squeezed into a fixed duration whatever its length, which had the Priest's attack
flickering past at thirty frames a second. The turn beat (`SecondsPerTurn`) is
deliberately *not* tied to it: that is the gap when nothing is animating, and dead air
should not grow with the animation.

**The log waits for the picture.** An attack resolves in the engine the instant it is
asked for — the roll, the damage and the death are all written before a frame of the
swing is drawn — so printing them straight away tells the reader the outcome while the
weapon is still going up. Each queued act remembers the log line it is the picture of,
and the narration is held there until the animation finishes: the rolled result and the
damage it dealt appear together, on the swing's last frame. Lines are delayed, never
reordered or dropped, and anything with no animation to wait for (a creature with no
art, a Dodge, the whole log during a probe) appears at once as it always did.

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
dragon sprite on a Green Dragon Wyrmling would be the display lying, which is why the
wyrmlings stayed circles until Brandon drew all five in their printed colours
(2026-08-21); the pack dragons are unmapped now.

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

**A single drawing is a complete token.** A strip is read as frames of `height × height`
across, so a sheet *narrower* than it is tall is not a strip at all — it is one standing
figure, and it is padded out to a square frame rather than rejected. That is the whole
setup for hand-drawn art of one creature: drop a single PNG in as `Idle.png` and the
creature stops being a lettered circle. It will not animate, and it does not need to —
every pose already falls back to `Idle` when its own strip is missing. Seventy
creatures ship this way as of 2026-08-21's second batch — it began with four (Gnoll
Warrior, Black Bear, Brown Bear, Giant Wasp), chosen because they were among the
most-drawn monsters in the pool and every one was a bare circle beside a party in
full animation, and Brandon has been retiring circles batch by batch since; the
Skeleton, Zombie and Cultist now wear his drawings rather than the packs that once
stood in for them. These
travel with the repo: the drawings are the project's own, so their folders are
whitelisted in `.gitignore` where the packs are not.

Three things such a drawing must get right, the first two inherited from the packs rather
than invented here.

**Feet on the bottom edge** of the canvas. Padding grows the canvas upward, so a drawing
floating in the middle of its image hovers above the ground.

**Drawn facing right.** The screen mirrors it when it should look left, so art drawn
facing left comes out backwards — a monster squared up to the party gets flipped away
from them. Flip it once when you install it rather than teaching the screen about
exceptions.

**Check the facing at 3x or larger, or by rendering it.** Judging a 64-pixel animal from
a thumbnail is unreliable, and two of the first batch went in backwards on exactly that
mistake — a Dire Wolf and a Giant Hyena, both side-on quadrupeds, both read the wrong way
round until a player said the wolf was facing away. Most of the set is front-facing,
where the flip does not matter; it is the side-on animals that bite. `RestingFacesLeft`
and the mirroring were correct throughout — the asset was simply backwards, which is
worth remembering before going to debug the code.

**Stature is drawn, not normalised — worth knowing, yours to decide.** `NominalStature`
is 64 and the board uses one shared pixel scale, so a figure drawn taller simply *is*
taller on screen; nothing rescales it. The installed set runs from 38 (Giant Eagle) to 92
(Hobgoblin Warrior), and the humanoids mostly sit at 60-67. Two Medium creatures drawn at
66 and 92 will stand noticeably different heights side by side. That is a look, not a
bug — the oversize ceiling in `ScaleFor` only engages near 96 pixels — so measure against
the set if you want them to match, and don't if you don't.

**Square it yourself if it is wider than tall.** The loader pads a *narrow* sheet,
because nothing narrower than one frame can be a strip — that inference is safe. A
*wide* sheet is genuinely ambiguous: `640x128` is five frames of a walk cycle, and
`64x46` is one drawing, and no rule tells them apart without guessing. The obvious
guess — "a strip's width is an exact multiple of its height" — has a false negative
sitting in these very assets (`Wanderer Magican/Charge_1.png` is 576x128, four and a
half frames wide), which is this project's oldest lesson about heuristics. So a wide
drawing is padded to a square canvas on disk, bottom-aligned and horizontally centred,
before it goes in. Otherwise it loads as a one-frame strip cropped to its left edge, and
you get most of a wolf.

**The battlefield has its own art too**, from the Tiled tilesets in the same free packs:

```
client/assets/sprites/Terrain/Ground_<Theme>.png       a strip of interchangeable 48px tiles, mixed per square
client/assets/sprites/Terrain/Wall_<Theme>.png         stands on a wall footprint (tree, rock pillar)
client/assets/sprites/Terrain/Low_<Theme>.png          stands on a low obstacle (boulder)
client/assets/sprites/Terrain/Difficult_<Theme>.png    one clump per Difficult Terrain square (brambles)
```

The themes are Woodland, Rocky and Barren; the per-theme files are Brandon's own art
and travel with the repo. The Craftpix pack cuts (Tree/Rock/Bush) that once backed a
theme missing its own drawing were deleted on 2026-08-20 — every theme carries its own
art now, and a theme without a drawing falls back to the flat colours. Numbered
variants (`_2` through `_9`) may sit beside any of the per-theme files: difficult art
mixes variants per square, walls and low obstacles per footprint, both chosen by
position hash so a fight always redraws the same field. The rocky and barren
difficult rubble ship four variants each; all three themes now carry difficult art,
so the dark wash remains only as the fallback for a theme without a drawing. Brambles are
deliberately *difficult* rather than an obstacle — brush is pushed through, rock is
gone around — which is why the woodland difficult slot wears what used to be its low
obstacle.

One theme is chosen per battlefield from the field's own shape, so a fight always redraws
the ground it had and the next fight — a different field — differs. **Each ground is a strip of four interchangeable tiles**, cut from the packs' own
tilesets and picked by seam continuity — how well a tile's opposite edges match, so it
repeats without a seam — and then by grain. The grain matters as much: a tile with a
distinct motif tiles seamlessly and still reads as wallpaper, because the motif lands in
the same place every sixteen pixels and the eye finds the lattice. Fine cobble and gravel
do not — and one tile over a whole field is a lattice however fine, so the board picks from
the strip per *ground* tile — three to a movement square each way, so the art is magnified
about 1.4 times rather than four — by hashing the coordinates — and turns and mirrors it, eight
orientations per tile, which is what stops the grain running the same way everywhere.
Hashed rather than rolled, so a square keeps its tile and its facing for the whole fight
instead of crawling underfoot. The ground recedes, the scenery carries the scene, and the board stays readable —
which is why there are no grid lines over it either. Difficult terrain wears the theme's
own drawing where one exists — brambles on the woodland — and keeps the dark wash where
none does yet, because art must not cost a player the one thing that square was telling
them: the wash is the floor, not a style choice.

Without the Terrain folder the board falls back to the flat colours and outlines it
always drew. (The original ground strips and scenery were cut from the Craftpix packs'
`Tiled_files/`; that provenance ended when Brandon's hand-drawn terrain replaced the
last pack cut on 2026-08-20.)

**Projectiles have a folder of their own**, because any archer fires the same arrow and
keying the sheet to a stat block would tie a Rogue's shortbow to the Skeleton Archer's
presence on disk. Since 2026-08-21 **every PNG in the folder loads by its file name**,
and the engine records each attack step's name (`CombatStep.AttackName` — recorded for
the reason `Ranged` is, so no client parses the narration), so per-weapon art is a
dropped file and never a code change:

```
client/assets/sprites/Projectiles/<Attack_Name>.png   that attack's own art (spaces as underscores)
client/assets/sprites/Projectiles/Arrow.png           any other ranged weapon attack
client/assets/sprites/Projectiles/Spell.png           any other spell attack
```

Brandon's set ships with the repo: `Arrow.png`, `Dart.png`, `Handaxe.png` — a
four-frame strip that tumbles in flight, since a multi-frame projectile loops as it
travels — and his crossbow bolt under all three crossbow names. (The spell generic was
named `Bolt.png` for a day, until that bolt needed the word: a file name a weapon could
plausibly claim is no name for a fallback.) All of it is optional — without a match a
weapon flies the arrow, a spell its generic then the arrow, and with nothing at all the attack simply swings and
lands with nothing drawn crossing the gap. Every sheet is drawn **pointing right**: the
client rotates it along the flight, the same convention the walk cycle's facing rests
on. Frames are square (height × height across), so a wide single drawing must be padded
to a square canvas on disk — a 56×6 arrow left unpadded would load as nine frames of
nothing much — and centred *both* ways, because a projectile rotates about its frame's
centre rather than standing on its bottom edge.

A machine without the folder — CI before the drawn set landed, a stale clone — draws
the circle tokens it always drew; every lookup is a fallback, nothing is load-bearing.
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

## Why this project *is* in SRDCombat.sln

**It was deliberately outside until 2026-08-15, and that was a mistake.** The stated
reasoning was that CI runs bare `dotnet restore`, `build` and `test` from the repository
root, which resolve the solution, so a client left out of it could never break the gate
protecting the engine on a runner with .NET 8 and no Godot.

The premise was false, and the plan doc's own Phase 7 trial had already recorded why:
`Godot.NET.Sdk` is a NuGet package, so **the build needs no Godot installed**. Checked
rather than argued — a cold build with `client/obj` and `.godot/mono/temp` deleted
resolves `GodotSharp.dll` from `~/.nuget/packages/godotsharp/4.4.0/lib/net8.0/`, never
from the Godot on `PATH`, and succeeds on net8.0 with 0 warnings. Two documents
disagreeing about a buildable fact is what settling it empirically is for.

What the exclusion cost was the whole point of having a gate: **5,065 lines — every line
a player actually touches — were never compiled by CI.** Nothing stopped a `Core`
signature change from breaking the client silently, and no test covers it from either
side. Building it is now the cheapest guard available; a test project for it is not yet
written, and that gap is real.

Two things the arrangement never cost, and still does not:

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
