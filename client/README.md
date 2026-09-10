# SRD_Combat Viewer

The Godot client. **The gauntlet is the default**, exactly as it is in the console: a
run of thirty fights with rests, experience, levelling and loot between them, autosaved
after every cleared fight. The party's turns wait for your mouse, every other side is
taken by the tactics policy, one turn per beat so you can watch what happens to you.
Between fights an interlude reports what the run reports — the rest taken, who returned,
who levelled, what was found — and a Continue button marches on. At each Long Rest a
Shop button opens the merchant's stall: every offer at its printed price, the purse in
the header, the unaffordable dimmed, a click buys, Back or Esc returns. The stall pages
rather than growing past the window (#704): only as many offers as fit above Back are
drawn, a "N more…" line names what is scrolled out of view, and the mouse wheel (one
offer) or Page Up/Down (one window) move the list — Back and the purse line are on
screen at every offer count, at any window size down to the floor below.
`--one-fight` plays a
single encounter instead; `--watch` keeps the original read-only screen, which resolves
one fight up front and lets you scrub through it. Either takes
`--spawn="Ogre, 2 Goblin Warrior"` to field exactly that cast instead of drawing from
the budget — a test aid (#456): comma-separated, optional leading count (up to 20),
names matched against the bestiary ignoring case, and a roster with any entry it cannot
parse is refused on screen with every failed entry named. Under `--one-fight`, that
refusal is the gauntlet's own between-fights screen and is wrapped rather than cut off
mid-remedy (#470); under `--watch` (or `--capture`, which implies it — see below) there
is no interlude to wrap into, so the refusal is the whole heading, one line, sized to
its own text rather than truncated (#486). In spawn mode `--level=1..5` sets the
party's level (default 3); a non-numeric or out-of-range value is refused the same way,
naming the value typed and the accepted range — never a silent fallback to 3 or a
silent clamp into range (#463). This client only ever recognises `--flag=value`, one
shell word — the console client's separate `--flag value` form is not accepted here,
and a `--spawn` or `--level` given without an `=value` (bare, or that unsupported space
form) is refused by name rather than silently read as if the flag were never passed
(#470). `--spawn` without `--one-fight` or `--watch` is refused too, since the gauntlet
loop draws its own roster every fight and would otherwise ignore the flag. The flagless
budgeted fight — no `--spawn`, no `--scenario` — reads `--level=1..5` and
`--difficulty=low|moderate|high` the same way, defaulting to level 3 Moderate when
neither is given (#443; before this the budgeted fight always ran level 3 Moderate
regardless of either flag). `--difficulty` alongside `--spawn` or `--scenario` is
refused rather than silently dropped, since a named cast has no budget for it to size.

`--scenario=<path>` plays one authored `.scenario.json` file instead — the battle
builder's third way in (#476), after `--spawn` and the flagless budgeted fight, and the
only new flag the whole battle-builder surface adds: it *plays* a scenario and never
authors one. It combines with `--seed=<n>` exactly as the other two paths do — the same
`(seed, scenario)` pairing the console's own printed seed exists for — and rolls and
prints one when none is given, so what is on screen is a complete bug report. Every
failure is refused by name and nothing else: no value given, no such file, whatever the
file's own JSON is wrong about (malformed, or naming a property this build's format does
not have), and any id it names — a monster, a species, a weapon — that this build's
content does not have. A mismatched content fingerprint is the one exception: it is
shown on screen as a notice rather than refused, because a scenario is a question asked
of the current build rather than a save that must match it exactly (design doc §5).
`--scenario` together with `--spawn` is refused rather than one silently winning — they
are two different answers to "what does this fight fight" — and, like `--spawn`,
`--scenario` without `--one-fight` or `--watch` is refused too, for the same reason: the
gauntlet loop never reads it.

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
(`--save=<path>` moves it), and `--continue` resumes it. `--save` given bare (present,
no `=value`) is refused by name on every one of this screen's own four launch modes
(one-fight, continue, create, fresh gauntlet), resolved once before any of them — the
same present-but-valueless policy `--spawn`/`--level` are already held to above — rather
than silently falling back to the default path the way it used to (#654); it is inert
under `--one-fight` (there is no run to save) but is still checked there, since a typo
is worth naming regardless of which mode reaches it. `--save` is not read at all under
`--watch`/`--capture` (a different screen, with nothing to save either way) — that is
unrelated to this refusal, exactly as `--level` and every other gauntlet-only flag are
already silently unread there too. Defeat does not touch the save —
the file keeps the state after the last fight the party *won*, so reloading is a retry.
`--level=1..5` starts a new run partway up — refused the same way spawn mode's own
`--level` is, above: a non-numeric or out-of-range value names the value typed and the
accepted range rather than falling back to 1 or clamping into range (#488). `--level`
together with `--continue` is refused too, rather than silently dropped, because a
resumed run has nothing for it to apply to — it re-resolves at the level the save's own
experience earned. `--create` (below) takes `--level` the same way a pregenerated run
does — the created party is always drafted at level 1 and resolved up to whichever level
the run begins at, exactly like a resumed save's own re-resolution — where it used to be
parsed and then silently discarded. A save that cannot be read is shown with its reason
and nothing is started, because silently beginning a fresh run would overwrite the file
being asked about.

On your turn:

| Input | Does |
| --- | --- |
| click a square | walk there — the engine charges movement and provokes what it provokes |
| click an enemy | attack with the hardest-hitting attack that reaches, never a bow point blank |
| **arrow keys** | move the cursor around the board — or the highlighted row, while a menu is open |
| **Enter** | take the highlighted menu row, or act on the cursor's square — the same thing a click would do |
| **Tab** | cycle the valid targets of whatever is armed; with nothing armed, arm the attack and start cycling — Enter then swings the best weapon at the cursor's target |
| **a letter** | the action whose button shows it: `D` Dodge, `R` Dash, `G` Disengage, `U` Stand Up, `E` Escape, `A` Attack, `C` Cast, `Q` Drink, `P` Give Potion, `V` Trade, `W` Second Wind, `S` Action Surge, `F` Rage, `K` Reckless, `M` Steady Aim, `X`/`Z` Cunning Dash/Disengage, `T` Trip, `H`/`J` Spark Heal/Harm |
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
no matter how the window was sized.) The floor is set once, in code
(`FightScreen._Ready`, `GetWindow().MinSize = new Vector2I(960, 540)`) — Godot 4 has no
`project.godot` setting for a window's minimum size (only its starting
`viewport_width`/`viewport_height`; see
[godot-proposals#7586](https://github.com/godotengine/godot-proposals/issues/7586),
which proposes adding one and confirms today's engine does not have it), so this is the
one place the floor can live and it already covers every `FightScreen` screen, the
merchant's stall included.

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

**The initiative panel pages rather than squeezing the log, past a capacity worked out
from the window's own height, at 1920×1080 too** (#305, Brandon's decision on the
round-2 review, 2026-09-10): the panel and the log share one column, and the panel used
to win it outright — every combatant it drew pushed the log's own start down, with
nothing capping how far. `InitiativePanelLayout.Fit` (the `ShopLayout.Fit` style of
#704/#710, cited by #727) caps that: past its own computed capacity the panel shows a
window of rows instead of the whole list, the window always starting at the active
combatant so whoever is acting is never hidden, sliding back only far enough to stay
inside the list once the active turn nears its end — hiding a few rows in the busiest
fights is accepted as the cost of a log that never shrinks below its own floor.
Combatants earlier in turn order than the window are named in the "INITIATIVE" header
itself ("2 above"); those later are named in the "COMBAT LOG" label itself ("2 below")
— the two are tracked and reported separately, since a window that starts mid-list can
hide combatants on both sides at once and a single combined count could not say which.
Neither notice reserves a line of its own: both fold into text that already draws
regardless of any count, which is what guarantees hiding a row always buys the log at
least one more line rather than occasionally buying nothing (a first-round bug: a
separately reserved "more below" line's own 16px could cost as much as the 19px a
hidden row frees, and once two independent roundings landed unluckily, hiding a row
gained the log nothing at all). None of these hidden rows are reachable by any input
during a fight: a click anywhere on the panel's own column is chrome, not a square, and
the mouse wheel is the camera's zoom, not a panel scroll — so unlike the merchant's
stall, which the wheel and Page Up/Down do scroll, this panel borrows only `Fit`'s
windowing shape from that pattern, not its scrolling half.

**The panel shows 8 rows at both resolutions** (`InitiativePanelLayout.MinRows`) — a
single constant now, not a per-height floor. An earlier version of this fix picked the
log's floor by a hard threshold on window height and derived the row count from that,
which turned out to be badly non-monotone: resizing the window by one pixel could swing
capacity from 21 rows to 1 (a second review found this before it shipped). The reason
runs deeper than a threshold picked wrong — showing one more row always frees 19px of
panel space, and a log line costs 17px, so *any* formula that lets the row count climb
smoothly as the window grows is provably forced to cost the log a line at the climb
itself, however the threshold is chosen. The fix is to not attempt the climb inside any
room a real window can reach: the row count is the flat constant 8 everywhere from just
below the smallest reachable window up through arbitrarily large ones, so there is
nothing left to dip.

Because 1080p and 720p now share one row count, they no longer share the same log floor
— 1080p's taller window turns those 8 rows into more log room than 720p's shorter one
does. A 14-combatant fight — a party of four plus a warband of ten, the worst case this
project fields — shows 8 rows and holds the log at 45 lines at 1080p (unchanged from the
threshold-based version, up from 38 before #305) and 24 lines at 720p (more than that
version's own 20, since 720p no longer pages as early: its previous count of 11 rows was
never a deliberate choice, only the old per-height floor constant working out to that
number incidentally):

| Combatants | 1080p shown | 1080p hidden | 1080p log lines | 720p shown | 720p hidden | 720p log lines |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 1 | 0 | 53 | 1 | 0 | 31 |
| 4 | 4 | 0 | 49 | 4 | 0 | 28 |
| 8 | 8 | 0 | 45 | 8 | 0 | 24 |
| 11 | 8 | 3 | 45 | 8 | 3 | 24 |
| 14 | 8 | 6 | 45 | 8 | 6 | 24 |

(The full 1–14 table is `InitiativePanelLayoutTests.ExactTableForBothResolutions`,
pinned row by row; two further rooms cited in review, 848px and 912px, are pinned
directly in `ExactValuesAtTheCitedIntermediateRooms`.) Once a fight's combatant count
passes 8, the log's line count stops moving entirely — every further combatant is
absorbed by the panel's own hidden count instead, which is the "no longer shrinks as
the initiative list grows" acceptance criterion made exact rather than merely bounded
below, and `InitiativePanelLayoutTests.CapacityAndLogLinesAreMonotoneInRoom` sweeps
every window height from well below 720p to well above 1080p checking that neither
value ever drops as the window grows.

**Verifying this at 1280×720 with a live capture is not currently possible.** The
client's window opens fullscreen (`project.godot`'s `window/size/mode=3`, with no
`window/stretch` entry to make a smaller logical viewport reachable), so a `--probe` run
always reports 1920×1080 regardless of any `--resolution` argument passed alongside it.
Closing that gap — a `--window-size` argument, or a temporary non-fullscreen mode for
captures — is #727's, not filed again here.

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

**A hit and a miss are visually identical no longer (#298).** A number rises off the
target and fades — red for damage, `LogHighlighter.Damage`'s own colour, the amount off
`CombatStep.Damage` rather than parsed back out of the sentence — and a swing that did
not connect rises the yellow word "Miss" instead of a zero, `LogHighlighter.Miss`'s own
colour, off `CombatStep.Hit`. Both facts are the engine's, recorded on the step for the
same reason `AttackName` and `Ranged` are: this project does not parse its own prose. A
death is already told apart from either — the body settles rather than flinching, and
stays dimmed once it has (see "Sprite art", below) — so the three outcomes the review
named (hit, miss, a kill) are three different pictures with the log covered. The number
rides on top of whatever beat is already playing (a swing, a flinch) rather than adding
one of its own — the review's complaint was the dead air *inside* the existing beat, not
too little time — and it advances on the same simulated-delta clock every walk and pose
already does rather than a wall-clock read, which is what keeps two `--probe` runs of
identical code in agreement (`FightScreen.ActiveRingNow`'s blink, #494/#518, is the
standing lesson: a raw `Time.GetTicksMsec()` read is what disagreed, not the animation
itself). The curve is `FightScreen.FloatingNumberMotion`, pinned by
`FloatingNumberMotionTests` the way the ring's own curve is pinned by
`ActiveRingBlinkTests`. Fog-safe by construction: it draws only for whichever token list
`DrawTokens` was actually given, and the play screen never gives that list a hidden
monster.

Faint blue squares are where a walk could end; ringed enemies are ones an attack
reaches; the **fog of war** shadows every square no party member can see (`PartyVision`
in `Game`: a wall blocks the line, sight is the whole party's union, and Unconscious or
Blinded eyes count for nothing), drawn smooth so its edge feathers rather than steps. A
monster standing in the fog is invisible — no token, no ring, no hover hint, no Tab
stop, and, while its row is inside the initiative panel's own visible window (#305: a
row the panel has paged out has no row drawn at all, fog or no fog), that row shows
`unseen` — until someone's line to it clears. All of it is advice, not rules — a click
anywhere is sent to the engine, and **a
refusal is shown with its code** rather than swallowed, because a refusal is the engine
explaining a rule. The second row is filtered by what the character *has*, which is
display: a shown button can still be refused, and absent is honest where inert would not
be. A line under the buttons reads out what is left to spend — slots, feature uses,
potions — straight off the engine's state.

**Hovering one of those blue squares previews the route a walk there would take if the
move is accepted (#303).** A brighter wash lights the squares `MovementRules.FindPath`
answers for the hovered square — the identical call, with the identical arguments,
`Encounter.Move` makes internally, so the preview can never show a route the walk
itself would not take (`PlayMode.HoverPreviewPath`). It appears the instant the
pointer settles on a reachable square, with none of the half-second rest the button
hints wait out (`PlayMode.HoverDelaySeconds`) — the reachable wash underneath it is
already instant, and a route is advice about the very click the wash is already
advertising, not a fact that benefits from being held back. "If the move is accepted"
is doing real work in that first sentence: the reachable wash's own eligibility is
what a route inherits, and that eligibility is geometry and budget, not every one of
`Encounter.Move`'s refusals — a Frightened character can still be shown a route into a
square `Move` will refuse with `movement.frightened` (moving closer to what frightens
them), because the wash that offers the square in the first place does not model that
clause either. The refusal still prints, with its code, the moment the click is
actually made; the preview is advice about the walk, never a promise the click cannot
be refused.

**What the fog does and does not hold.** A square along the route that the party
cannot presently see is dropped from what is *drawn* — to the same standard a hidden
monster's token, ring and hover hint are already held to, so a route's picture never
shows more ground than the fog already would. What fog does **not** do is change the
route itself: `HoverPreviewPath` asks for it with the same full knowledge of every
combatant `Encounter.Move` already has, so an unseen occupant blocking the cheaper
corridor can still make the route (and the reachable wash it sits inside, which was
never fog-aware either) go the other way — the shape of an available route can, in
that sense, hint at something the party has not seen. This is not new to the preview:
the reachable wash it sits inside has always been computed the same way. Both are
being tracked for a proper fix rather than papered over here.

**A previewed route that would cost an Opportunity Attack says so, on the square it
fires entering (#301).** Before this, threat was invisible until it happened — the
review that opened #301 named the game's central positioning rule as having no display
at all, and the two advisory layers that already existed (the reachable wash, a ring on
an already-red token) as "the least visible things on screen". `PlayMode.ThreatenedSteps`
walks the route's own full, unfiltered squares (`HoveredPath` — the same route
`_previewPath` fog-trims for drawing, before that trim) one real step at a time from
the mover's own position, and asks `MovementRules.FindOpportunityAttackers` — never a
re-derived reach check — whether that exact step provokes; a step it fires on is
outlined in a saturated warning colour (`Palette.ThreatMark`), opaque rather than
another translucent layer, so it reads as a distinct warning rather than one more shade
of the same advice. The mark lands on the square *entered* by the provoking step, not
the square left, though the printed trigger fires "right before it leaves your reach"
— a deliberate choice: the route is already drawn as a sequence of squares to step
*into*, so marking the square left would put the warning one square behind the step
that actually costs the swing.

**Fog holds two ways at once, not one.** Only enemies the party can presently see may
threaten a square at all — a monster nobody has spotted contributes no mark, the same
standard a hidden occupant's token, ring and hover hint already meet, and showing one
would leak its presence before the fog itself would (the #732 leak shape this is the
other side of). Separately, a provoking step whose own square the party cannot
presently see is dropped from what is *reported*, never from what is *walked*: judging
adjacency against the already fog-trimmed squares (this feature's first version) could
silently drop a real provoke whenever the hidden interior square was the one actually
being left — a visible square, ten feet from an enemy, stepping into a fogged square
five feet away, then on to another visible square ten feet away again leaves that
enemy's reach exactly once, on the fogged square, and a check that never saw it
compared the two visible ends directly and found no change of reach at all. Walking the
whole route and dropping only the *reported* square keeps that adjacency correct while
still never drawing a mark past what the route itself shows.

Recomputed everywhere the route itself is: every `UpdatePreviewPath` call, so a routed
keyboard action, a camera change or `RefreshAfterAction` never leaves a stale mark on
screen, and gated by the exact same `PreviewMayShow` the route is — armed targeting or
a menu over the board shows neither. **Drawn after the fog, and inset from the
square's edge** (`ThreatMarkInsetPixels`, `PlayMode.Draw.cs`): a threatened square is
never fogged by construction (the drop above), so there is nothing left for the fog
wash to dim; and the keyboard cursor's ring draws the identical bordered square at the
identical width on whatever square it sits on, so draw order alone cannot save the mark
— two same-width strokes around the same rectangle paint the same pixels, and the later
one wins whichever it is. Shrinking the mark's square is what lets both survive as
concentric outlines when the cursor parks on a threatened step. Three-theme legibility (Woodland, Rocky,
Barren) is tracked as its own issue, #735: the theme is a deterministic hash of a
battlefield's own geometry with no override (#731 found this for the path preview
already), so there is no cheap way to force all three for one seed's probe capture.

**Arming an attack or a spell shows what it can reach before the click commits it
(#302).** The movement family above (the reachable wash, the route preview, the
threat mark) all answer "where could I walk"; this is the same seam's other half,
for "what could I hit". `PlayMode.Input.cs`'s `UpdatePreviewPath` recomputes
whichever family applies — the two are mutually exclusive by construction, since
`Armed` being an attack or a spell is exactly the condition under which the movement
family's own `PreviewMayShow` already answers false.

*Range.* An armed attack's envelope (`PlayMode.AttackRangeEnvelope`) is every square a
target could stand in for the click to actually reach, split into a normal band and —
for a ranged weapon with a printed long range — a fainter far band paying
Disadvantage, straight off `CombatAttack.CanReach`/`IsAtLongRange`. Tab's cold arm
(no weapon named) reads generously, the same way `AttackChoice.BestFor` — the seam
the click itself goes through — already does for that case (`TargetChoice`, by contrast,
admits any living enemy when no attack is named): the union of every carried attack's own reach, normal band only, since
combining several weapons' long-range bands into one picture could show a
Disadvantage warning that belongs to a weapon the click never ends up using. A
non-area spell gets the same treatment off its own printed
`SpellDefinition.TargetRangeFeet` (`PlayMode.SpellRangeEnvelope`) — empty for a
self-ranged spell, and empty for Sight or Unlimited range too, since a wash over the
whole board would say nothing the spell's own printed range text does not already.

*Area.* An armed area spell shows exactly what `AreaTargeting.Cover` would cover for
the hovered square — the identical call `Encounter.CastSpell`'s own `SaveVictims`
makes internally (`PlayMode.AreaCoverage`), never a client-side re-derivation of the
geometry. The creatures it would actually catch are marked with a ring, filtered the
same way `Encounter.CreaturesIn` filters them (alive, and occupying a covered
square) and narrowed to enemies only when the spell's own `EnemiesOnly` selector says
so (#601's reading, asked here the same way `SaveVictims` asks it).

*Out of range.* A hovered target outside the envelope is marked — a border on the
square, dimmer and less saturated than the Opportunity-Attack threat mark so the two
are never confused, plus the same refusal code the click would actually produce
named in the status line. The code is never invented: a chosen weapon's own
`attack.out_of_range` (`Encounter.Attack`'s check), Tab's cold arm's own
`client.no_attack` fallback when nothing carried reaches (`PlayMode.ActivateSquare`'s
own path, mirrored by `PlayMode.AttackOutOfRangeCode`), or a spell's own
`spell.out_of_range` (`Encounter.CastSpell`'s check, mirrored by
`PlayMode.SpellOutOfRangeCode`) — the same two questions, "does the model express
it" and "what would the engine actually say", this project asks everywhere else.

**Fog holds for the targeting family too, on both of its own halves.** The range
envelope is ambient board geometry, the same standing the reachable wash already
has — not fog-trimmed, since a hidden square inside it reads through the fog's own
shadow exactly as the reachable wash already does, never any brighter. The area
coverage is specific, committed advice about one aim point instead, held to the
route preview's own stricter standard (#732): a square the party cannot presently
see is dropped from what is *drawn*, though `AreaTargeting.Cover` itself is asked
with full knowledge, so the coverage's *shape* can still hint at unseen geometry the
same qualified way a route's shape already can. A caught creature is reported only
when it is both inside the covered squares and not itself hidden — the same standard
a hidden occupant's token, ring and hover hint are already held to, so a Sphere
dropped over fogged ground never announces a monster standing in it before the fog
itself would. This is asked once, at the square level (a hidden creature's own
square was already dropped from what is covered), rather than a second time per
creature: the earlier shape asked both and checked only the creature's anchor
square, which could have hidden a multi-square creature whose *other* occupied
square genuinely sat in visible, covered ground.

**Four wiring gaps Codex's adversarial review found, each fixed at its own seam.**
`_pointer` used to be one field doing two jobs — the tooltip's own jitter-filtered
rest position, and what every refresh (a routed keyboard action, a camera change,
`RefreshAfterAction`) read the hovered square from — so a pointer that drifted a
couple of pixels across a grid line, well under the tooltip's own three-pixel jitter
threshold, could leave a refresh recomputing the *old* square's route for a click
about to land on the new one. `_pointer` now tracks every raw motion sample
unconditionally (`PlayMode.TrackedPointer`), and `_hintAnchor` is the tooltip's own
separate, still-jitter-filtered rest pixel. A reachable square sitting under the
initiative/log panel or the bottom banner strip used to still light a route, though a
click there hits the chrome and never the square underneath it
(`RouteClick`'s own `OverOverlay` check); `PlayMode.PreviewMayShow` now asks the same
`OverOverlay` the click path does. And a mouse-driven cancel — clicking to abandon an
armed target, or clicking outside an open menu — used to leave the preview empty
indefinitely: `ClearPending` popped focus back to Board but never recomputed, and a
same-square hover afterward found nothing changed to react to, since the *square*
never moved — only whether a click on it would. `ClearPending` (and `PerformClick`'s
own shared tail, for the menu-dismissal paths that never call it) now recompute the
preview every time, unconditionally, exactly as `ArmTargeting` already did for the
arming side.

Arguments go after Godot's `--` separator. `--seed=<n>` picks the run — the same promise
the console client makes, that a seed is a complete bug report; without one the seed is
fresh, and it is always in the heading. (A `--capture` or `--probe` run falls back to a
fixed seed instead, because a verification image must not change between runs.) A
non-numeric `--seed` is refused by name rather than silently rolling a fresh one anyway
(#489) — of every flag here, `--seed` is the one place a quiet fallback would be worst:
it exists precisely so a typo cannot cost you the run you meant to reproduce.
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

The tokens can be animated pixel-art figures instead of circles, in principle — every
drawing actually shipped with the repo today is Brandon's own single still per pose, so
what "plays" is that still held for its pose's whole duration rather than a moving
cycle; a multi-frame Craftpix pack, downloaded separately, would animate through its own
frames the same way. Five poses queue in the log's own order so each lands where it
belongs: an idle loop while
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
killed goblin as a smear on the floor. Almost everything shipped today is Brandon's own
still, run through the committed pipeline; the maps are curated in `SpriteLibrary`:
party art by class name, monster art by **exact** stat-block name, and anything
unmapped keeps the circle-and-letter token — a red dragon sprite on a Green Dragon
Wyrmling would be the display lying, which is why the wyrmlings stayed circles until
Brandon drew all five in their printed colours (2026-08-21).

**A regeneration can silently change a sheet's canvas size (#467)** — the shipped
sheets predate the pipeline's current settings, so running it against a master today
does not reproduce what is already shipped (PR #461's Ogre went 169×169 → 119×64 this
way, caught only by Brandon's own eyes in a live fight). `tests/SRDCombat.Viewer.Tests`
pins every shipped sheet's dimensions and frame count against a committed manifest and
fails the suite on drift; see `tools/asset_pipeline/master_to_sprite.py`'s module
docstring ("Geometry contract") for the full reasoning and the regeneration steps.

**The free Craftpix character packs are retired from the shipped roster as of #295.**
Every pool name that once mapped to one — the eight party classes without a drawing
yet (Paladin, Monk, Bard, Ranger, Druid, Wizard, Sorcerer, Warlock) and seven monster
names (Goblin Boss, Gladiator, Knight, Mage, Archmage, Priest, Priest Acolyte) — had
its mapping removed rather than left pointing at a folder that can never ship: Craftpix's
free license permits using the art in a game but not redistributing the assets, and
this repo is public — the same line the SRD PDF sits behind — so a release build never
carried that art regardless of what the source said. Those fifteen render as circles
until Brandon draws them. The loader itself still reads whichever strips a mapped
folder holds, multi-frame or single, so nothing stops a future pack-based mapping from
working the way the old ones did — download a free set from
[craftpix.net](https://craftpix.net/freebies/), unpack it, and point a `SpriteLibrary`
entry at its folder — but the maps committed today hold none, and each folder is still
gitignored so it never becomes load-bearing for anyone who hasn't downloaded it
themselves. A character's folder of animation strips, drawn or packaged, sits at:

```
client/assets/sprites/<Character>/Idle.png (Walk.png, Attack.png, Hurt.png, Dead.png)
```

`Idle.png` is the only one a character cannot do without — it is what a standing token
shows, and what the figure is measured from. Everything else degrades on its own: no
`Attack.png` and that creature never swings, no `Hurt.png` and it never flinches, no
`Dead.png` (true of every single-frame drawn folder, and of the old Priest packs
before they were retired) and it lies its idle frame on its back.

**A single drawing is a complete token.** A strip is read as frames of `height × height`
across, so a sheet *narrower* than it is tall is not a strip at all — it is one standing
figure, and it is padded out to a square frame rather than rejected. That is the whole
setup for hand-drawn art of one creature: drop a single PNG in as `Idle.png` and the
creature stops being a lettered circle. It will not animate, and it does not need to —
every pose already falls back to `Idle` when its own strip is missing.
Seventy-two creatures ship this way as of 2026-08-25's batch (#295: Guard Captain and
Warrior Infantry) — it began with four (Gnoll Warrior, Black Bear, Brown Bear, Giant
Wasp), chosen because they were among the most-drawn monsters in the pool and every
one was a bare circle beside a party in full animation, and Brandon has been retiring
circles batch by batch since; the Skeleton, Zombie and Cultist now wear his drawings
rather than the packs that once stood in for them. These
travel with the repo: the drawings are the project's own, so their folders are
whitelisted in `.gitignore` where the packs are not.

Three things such a drawing must get right, the first two inherited from the packs rather
than invented here.

**Feet on the bottom edge** of the canvas. Padding grows the canvas upward, so a drawing
floating in the middle of its image hovers above the ground.

**Drawn facing right.** The screen mirrors it when it should look left, so art drawn
facing left comes out backwards — a monster squared up to the party gets flipped away
from them. Flip it once when you install it rather than teaching the screen about
exceptions. As of #457, "flip it once when you install it" is no longer a step someone
has to remember by hand: `tools/asset_pipeline/master_to_sprite.py`'s `MASTER_FACING`
table declares which masters are painted facing left, and the pipeline mirrors exactly
those, once, at generation — never on the master file itself, and never guessed from
pixels. A stem absent from the table is assumed right-facing, the common case.

**Check the facing at 3x or larger, or by rendering it.** Judging a 64-pixel animal from
a thumbnail is unreliable, and two of the first batch went in backwards on exactly that
mistake — a Dire Wolf and a Giant Hyena, both side-on quadrupeds, both read the wrong way
round until a player said the wolf was facing away. Most of the set is front-facing,
where the flip does not matter; it is the side-on animals that bite. `RestingFacesLeft`
and the mirroring were correct throughout — the asset was simply backwards, which is
worth remembering before going to debug the code. #457's full audit found the same bug
in fifteen more side-on paintings (the Ogre and Goblin Warrior are the two Brandon
actually caught in play) and confirmed it as a habit rather than a one-off — Brandon
tends to paint a side-on creature facing left, and only Dire Wolf and Giant Hyena had
ever been caught and hand-corrected before `MASTER_FACING` existed to do it
mechanically. Giant Hyena, Axe Beak, Owlbear, Giant Bat and Winter Wolf were already
flipped by hand at the shipped-sprite level before this table existed; each has a
master, so each is declared "left" in `MASTER_FACING` anyway, so a future regeneration
keeps producing the same, already-correct facing rather than reverting to the raw
painting. Dire Wolf has no master file to key a declaration by (one of eleven shipped
sheets with none — a separate, pre-existing gap #457 only noted in passing) and stays
correct only because its shipped sheet was hand-flipped directly; it cannot go through
this pipeline until a master exists for it.

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
the ground it had and the next fight — a different field — differs. **Each ground is a
strip of seven interchangeable tiles**, Brandon's own 48-pixel art (the Craftpix ground
cuts this replaced are gone — see below), picked by seam continuity — how well a tile's
opposite edges match, so it repeats without a seam — and then by grain. The grain matters
as much: a tile with a distinct motif tiles seamlessly and still reads as wallpaper,
because the motif lands in the same place every sixteen pixels and the eye finds the
lattice. Fine cobble and gravel do not — and one tile over a whole field is a lattice
however fine, so the board picks from the strip per *movement square* — one tile fills the
square now, at the same ~1.4x magnification the old Craftpix layout got from three tiles
to a square each way, so the resolution argument that shape existed for is moot — by
hashing the coordinates — and turns and mirrors it, eight orientations per tile, which is
what stops the grain running the same way everywhere.
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
`--watch` — a capture of a fight nobody is playing is the watch screen's job. A refused
`--spawn`/`--level`/`--seed` never reaches a fight to render: `--watch` alone shows the
reason in the heading with no snapshots to scrub, and `--capture` prints the same reason
to stdout and exits non-zero, writing no PNG at all — never the blank frame reported as a
successful capture that this used to write (#486), and never a live, heading-only window
left running with the reason unsaid, which a refused `--seed` still did in capture mode
until #602 reordered the check that catches it. A non-numeric or out-of-range `--at`
is refused the same way, to stdout with a non-zero exit, naming the value and the turn
range actually resolved — never the silent "last turn" fallback or the silent clamp into
range this used to do (#489). `--seed`, `--at` and `--capture` given bare (present with no `=value`)
are refused by name too, the same shape #470 already holds `--spawn`/`--level` to —
`-- --seed` used to roll a fresh seed silently and `--capture=... --at` used to silently
capture the last snapshot, both indistinguishable from the flag never having been passed
at all until #602 closed the gap. A bare `--capture` on its own (without `--watch`) used
to be indistinguishable from `--capture` never having been passed at all one level higher
still — `Main.cs`'s own routing read it as absent and opened the ordinary gauntlet
instead of this screen — until #654 closed that gap too, routing on presence alone and
refusing the bare flag here, on this screen, the same way a bare `--at` already is.

### The probe

```bash
godot --path client --display-driver x11 -- --probe=<directory>
```

The play screen's verification loop: it drives the screen through the real input path —
synthesized clicks through the viewport, not calls around the input layer — and captures
a PNG after each step. It needs a real, reachable X display and Godot's `x11` driver — a
capture is a rendered frame, and there is nothing to render headless. Which display is
up **moves** on this machine (`:0` on 2026-08-26/27, `:1` on 2026-09-02), so take it from
`.claude/skills/probe-diff/scripts/find-display.sh` rather than typing one; the
`probe-diff` skill is the baseline-then-compare procedure around this loop.

**It is two invocations, not one**, because the eight focuses #327's refactor moves
(`docs/2026-08-26-playmode-refactor-design.md`, §4) do not all fit under one seed at one
level:

```bash
# The main run: seed 1, the gauntlet's default level 1. Reaches six of the eight
# focuses plus every run-lifecycle capture.
godot --path client --display-driver x11 -- --seed=1 --probe=<directory>

# The Slot menu needs a caster holding spell slots at more than one level for the same
# spell — no level 1 character has that. --one-fight already fixes the party at level 3
# (FightScreen.ResolveFight), so this is the second half of "the probe" rather than a
# second full-gauntlet seed. It quits as soon as the Slot menu is reached (or its own
# turn budget runs out).
godot --path client --display-driver x11 -- --one-fight --seed=1 --probe=<directory>
```

Both finish in seconds, not minutes — measured together at 16 s end to end (2026-08-26).
That was not always true: earlier the same night the main run's play-out loop alone took
~15 minutes, because `ClickButton` (below) never actually clicked anything, and turns
advanced only via `NothingLeftButEndTurn`'s auto-end-turn, one `_pace`-delayed tick at a
time. Fixing `ClickButton` fixed the wall-clock cost as a side effect, not a change made
for that reason.

Both write into the same directory; their capture names do not collide. What the main run
produces, in order: `run-0-interlude`, a commanded turn (`play-1-turn-ready`), the quit
confirm (`play-1b-quit-confirm` — Esc asks, a key that is not Esc backs out unharmed), an
unavailable action attempted on purpose (`play-2-stand-up-not-offered` — Stand Up while
not Prone; see below, this is not a refusal), a hover hint
(`play-2b-hint`), Tab-arming (`play-2c-tab-armed`, and its own path-preview assertions
against a square hovered just before the arm), the move step's own path preview
(`play-2d-path-preview` — hovering the square the walk below is about to take, and a
camera zoom checked against it), whichever reachable square (if any) previews a route
that crosses an Opportunity-Attack threat (`play-2e-threat-preview`, #301 —
`TryCaptureThreatPreview` searches every square `_reachable` offers by independently
recomputing the route and its threat, never by reading the live field it is checking,
tried on the opening commanded turn and again on every later one in the fight-1
play-out loop below until one lands or the fight ends; `ReportSkip` if none ever does),
the same square with the keyboard cursor parked on it
(`play-2f-threat-under-cursor` — proving the mark still shows once the cursor's own
ring shares the square, #301), a walk and an attack (`play-3-moved`, `play-4-attacked`),
a feature (`play-5-feature`), End Turn
(`play-6-turn-ended`), a second commanded character's cast flow if it is a caster's turn
(`play-7-spell-menu`, `play-8-cast`), then plays fight 1 out to its end, capturing along
the way whichever party member carrying more than one weapon takes a turn first — Brenna,
Korrin and Sable all do at level 1, Aldous alone does not — with the Attack menu open
(`play-9-attack-menu`), the
Outcome card the instant it appears and before anything dismisses it
(`run-9-outcome-card`), the post-fight interlude or defeat screen
(`run-9-after-fight`), and — since seed 1 clears fight 1 and the ladder rests Long before
fight 2 — the merchant's stall (`run-10-shop`). Seed 1 clears fight 1 this way; the
default seed loses it — both ends of `HandleFightEnd` have been watched. The one-fight
run adds `play-9-spell-menu` and `play-9-slot-menu`.

**An exception inside a probe step fails the run, loudly and non-zero.** `PlayMode.RunProbe`,
`CreateMode.RunProbe` and `WatchMode.CaptureAndQuit` used to be `async void`, so a throw
inside any of them vanished into Godot's own unhandled-exception logging — the probe
either hung (nothing left to reach `GetTree().Quit()`) or, further along, exited 0 having
silently stopped short. `ProbeFaults.FireAndObserve` (#322) now watches each one's task:
a fault is printed (`probe: crashed — …`, with the exception) and the run exits `1` — the
fire-and-forget call at the Godot lifecycle boundary stays, since `RunProbeIfAsked` and
`OnReady` cannot themselves be `async`, but nothing thrown downstream disappears again.

**A capture that could not be written is a fault, not a printed line** (#705). `CaptureFrame`
(`FightScreen.cs`, duplicated in `CreateMode.cs`) used to print `could not save … : {error}`
on a failed `Image.SavePng` and carry on regardless — a probe pointed at a missing or
unwritable output directory still exited 0, with every step "succeeding" and no PNG on
disk for any of them. It throws now (`CaptureOutcome.FailureMessage` is the pure decision
behind the throw, pinned by `CaptureOutcomeTests` with no Godot engine at all), which
`ProbeFaults` turns into the same crashed-probe exit a thrown assertion already takes.

**Every *required* step asserts a predicate before it captures, not after** (#705,
tightened across three more #719 review rounds — see below for exactly which predicates
each round found missing). `ProbeExpectation` (`client/ProbeExpectation.cs`) has ten
shapes, over a plain `ProbeSnapshot`: `FocusIs` (the focus layer expected), `NoticeCodeIs`
(the refusal code expected, most often "no refusal at all" — `null`), `NoticeCodeIsOneOf`
(a refusal code expected to belong to a curated set — not a shared prefix: no prefix
actually covers everything `Encounter.CastSpell` or `Encounter.Attack` can return,
`target.unseen` and the Action/Bonus-Action/Reaction "already spent" codes included,
#719's fourth review), `Unchanged<T>` (a resource — an actor's
position, hit points, movement, and every resource its turn's economy tracks — the step's
own action must not have touched), `Changed<T>` (the mirror: a resource — the active
combatant's `Id`, the round — the step's own action must actually have moved),
`EqualsExpected<T>` (an observed value that must equal a specific target — the actor
landing on the exact square clicked), `Decreased` (a count that must have gone down),
`NonEmpty` (a string that must be present and non-empty, checked *before* it is trusted
as something else's expectation), `NoticePresent` (some notice must have printed, code
unspecified), and `AnyOf` (passes when at least one of several expectations does — the
logical OR `PlayMode.Assert`'s own implicit AND across its parameter list cannot
express). A failed predicate throws, naming the step, the same
fault path as a crash — never `ReportSkip`, below.

**Why the plainer pair — `NoticeCodeIs(null)` plus `FocusIs(Board)` — is not, by itself,
proof that a click did anything** (#719's first review round): both hold exactly as truly
*before* a click that turns out to be a no-op as after it — the shape `play-2` itself was
found in. So every step whose click is expected to succeed also asserts the click's own
effect, per step:

| Step | What it asserts |
| --- | --- |
| `play-1-turn-ready` | `FocusIs(Board)`; `NextCommandedTurn` itself throws if no commanded turn ever arrives |
| `play-1b-quit-confirm` | `FocusIs(QuitConfirm)` |
| `play-2-stand-up-not-offered` | `NoticeCodeIs(null)`, `FocusIs(Board)`, and `Unchanged` on the commanded actor's position, hit points, movement, Action, Bonus Action, Reaction and remaining attacks — see below |
| `play-2b-hint` | `NonEmpty` on the hovered button's *registered* hint (a broken registration is a fault before it is ever compared against anything), then `EqualsExpected` — the hint text actually produced equals that registered hint |
| `play-2c-tab-armed` | `FocusIs(Targeting)` and `EqualsExpected` (the first Tab armed `TargetKind.Attack` specifically, not a routing regression's Potion or spell); with more than one *visible* enemy, `Changed` on the aimed target after the second Tab (with only one, `ReportSkip` — nothing to cycle to) |
| `play-2c-tab-armed-preview` (#303, PR #731 rounds 1-2) | A reachable square is hovered *before* Tab arms anything; `NonEmpty` on the preview it produced, so the later empty check has something real to have lost. After Tab: `EqualsExpected` — the preview equals the empty string, with no mouse motion in between, proving `ArmTargeting` itself (not a stray hover) cleared it. After the matching Esc disarms: `NonEmpty` again — the preview returns with the pointer still on the same square, proving `PlayFocusRouter.Route`'s own `Perform`-triggered recompute, not a coincidence. A second cycle then re-arms with Tab and *clicks* the still-hovered square to cancel (nobody stands there, so `ActivateSquare`'s Attack branch takes its `ClearPending`-and-do-nothing path rather than swinging): `FocusIs(Board)` and `NonEmpty` again, proving the mouse-driven cancel restores the preview too, not only Esc's keyboard-routed one (round 2 review) |
| `play-2d-path-preview` (#303) | `FocusIs(Board)` (a hover must not itself open or close anything); `NonEmpty` on the expected route (a broken expectation is a fault before it is compared against anything); `EqualsExpected` — the previewed path (`PlayMode.HoverPreviewPath`'s own field) equals `MovementRules.FindPath`'s answer for the same square, both rendered through `PathAsText` since `EqualsExpected<T>` compares by `EqualityComparer<T>.Default` and two structurally-equal lists are not `Equals` by that measure |
| `play-2e-threat-preview` (#301) | `TryCaptureThreatPreview` searches every square `_reachable` offers by asking `HoveredPath` and `ThreatenedSteps` fresh per candidate — never the live `_threatenedSteps` field, the thing under test, which a PR #734 review round found could mask a wiring defect that marks nothing as a skip rather than a fault. Once a candidate is found and hovered for real, `NonEmpty` on that independently-recomputed expectation, then `EqualsExpected` — the live `_threatenedSteps` equals it. Tried on the opening commanded turn and, if that finds nothing, again on every later commanded turn in the fight-1 play-out loop; `ReportSkip` only once, after that loop, if no turn in the whole fight ever found one |
| `play-2f-threat-under-cursor` (#301) | No fresh assertion — the keyboard cursor (`_cursor`) is set directly onto the same threatened square `play-2e-threat-preview` just proved, and the capture exists to show, by eye and by `probe-diff`'s pixel box, that the threat mark's own ring and the cursor's ring are both still visible on the same square (`PlayMode.Draw.cs` insets the mark by `ThreatMarkInsetPixels` for exactly this reason — at the cursor's own geometry the two identical bordered `Rect2`s occlude each other whichever is drawn last). `_cursor` is restored immediately after |
| `play-5b-camera-zoom-preview` (#303, PR #731 round 1) | No capture — assertions only, sandwiched between a wheel zoom and its exact inverse so nothing survives into `play-3-moved`'s own frame. `Changed<float>` on `GridLeft` (the zoom must actually have moved the mapping); with the pointer's screen position unchanged by the zoom itself, `EqualsExpected` — the preview matches `MovementRules.FindPath` for whichever square that fixed pixel now maps to, proving `HandleCameraInput`'s own consumed branch re-triggered the preview rather than leaving the pre-zoom route standing |
| `play-3-moved` | `NoticeCodeIs(null)`, `FocusIs(Board)`, `EqualsExpected` (actor position == the clicked square), `Decreased` (movement remaining) |
| `play-4-attacked` | the target is the nearest *visible* enemy (`PartyVision`, not `NearestEnemyOf`'s fog-blind pick); `EqualsExpected` that `TokenAt` agrees before the click; after it, `FocusIs(Board)` and `AnyOf` — a log entry naming both the actor and the target, or a refusal from Attack's own curated code set; the attack can legitimately refuse, but doing nothing (or an unrelated action) is a fault. Then (#299) two `EqualsExpected` checks the frozen suite cannot reach at all — `PlayMode`, its `TokenFrom`/`BarColourFor`/`RowFor` calls, and every `Token` they build only ever exist inside a live Godot node (#490/#190): recomputing `FightScreen.TokenFrom` off the actually-struck `Combatant` and checking its `IsBloodied` still equals `Combatant.IsBloodied` unchanged, and that `BarColourFor` (the board's own hp-bar fill) equals that same token's `RowFor(...).StateColour` (the panel's own hp-line colour) — the live proof that the two surfaces cannot independently decide a combatant's health band |
| `play-5-feature` | none — optional coverage, `ReportSkip` when no second-row feature exists; can legitimately refuse when it does |
| `play-6-turn-ended` | `NoticeCodeIs(null)`, `FocusIs(Board)`, `Changed` (the active combatant's `Id` or the round — `Id`, not `Name`: two same-named combatants acting consecutively must not read as "no change") |
| `play-7-spell-menu` | availability checked first (`ButtonOffered("Cast")`, `ReportSkip` if not this turn); when offered, `FocusIs(SpellMenu)` — a fault, not a skip, if Cast was offered and still failed to open it |
| `play-8-cast` | `Armed.Spell.Id` equals the spell row 0 actually represents (`CastableSpells(caster)`, recomputed the same way `DrawSpellMenu` filled the row — not merely "some spell got armed"); then `FocusIs(Board)` and `AnyOf` evidence attributed to that spell specifically — a new log entry naming it, or a refusal from `NoticeCodeIsOneOf(CastSpellRefusalCodes)` (curated from the engine, not a `"spell."` prefix — `target.unseen` and the shared Action/Bonus-Action/Reaction codes do not start with it); a spell menu confirmed open but showing zero rows is a fault, not unavailable coverage |
| `play-9-attack-menu` | availability checked first (`ButtonOffered("Attack")`; not offered this frame just loops to try again, no skip); when offered, `FocusIs(AttackMenu)` — a fault if it fails to open |
| `run-9-outcome-card` | gated by `_focus.Holds<Outcome>()` before capture; three outcomes distinguished, not two — still running when the safety budget runs out is `ReportSkip` (the #180 stall guard below then faults on it separately), completed-and-shown is fine, and completed-with-Outcome-never-displayed (the old `HandleFightEnd` bypass) is a fault of its own, never silently accepted on the way to `run-9-after-fight` |
| `run-9-after-fight` | the play-out loop faults if it exhausts its safety budget still mid-fight, rather than capturing that as "after the fight" (#180's own shape) |
| `run-10-shop` | `FocusIs(Shop)` |
| `run-0-interlude` | none — nothing has acted yet; the branch condition (`_phase == Phase.Interlude`) is itself the guarantee |
| one-fight `play-9-spell-menu` | same availability-then-`FocusIs(SpellMenu)` shape as `play-7-spell-menu` |
| one-fight `play-9-slot-menu` | availability established by `castable.FindIndex(...) >= 0` (a spell castable at more than one slot level exists this turn — `ReportSkip` otherwise); once found, `FocusIs(SlotMenu)` is a fault if it fails, and a spell found in `castable` but missing from the drawn `_menuRows` is itself a fault (a "the two are populated in the same pass" assumption broken), never folded into the same skip as "no such spell exists" |

`play-2-stand-up-not-offered` is the named instance (#521): `ClickButton("Stand Up")`
while the commanded character is not Prone, which `TurnOptions` never offers a button
for, so the click finds nothing and the capture was — confirmed live — a byte-copy of
`play-1-turn-ready`, under a name (`play-2-refused`) that claimed a refusal it never
produced. Renamed, it now asserts exactly what is true today: no refusal, the focus
unchanged, *and* the commanded actor's position, hit points, movement, Action, Bonus
Action, Reaction and remaining attacks (`Combatant.Features.AttacksRemainingThisAction`)
all unchanged — widened at #719's second review round from Action alone, which a
misrouted click spending only a Bonus Action would have passed undetected. Retargeting
this step onto a refusal the probe can actually reach is #521's still-open decision, not
this one's.

**`FocusIs(Board)` alone is not evidence an action resolved**, either (#719's second
review round): deleting a click's own handler entirely can still leave the board
uncovered, the same way a no-op leaves the focus and the notice untouched. `play-4-attacked`
and `play-8-cast` both need positive evidence the click did something — the combat log
gaining an entry (a resolved attack or cast) or a notice being printed (a refusal) — and
`AnyOf` is exactly that "one of these, not neither" check.

**A step the probe could not reach — or could not confirm it reached — says so, it does
not skip in silence — and a step that *was* reachable but whose own effect failed is a
fault, never dressed up as the same "could not reach" skip** (#705, sharpened across
#719's review rounds). Whether a character brought a feature, whether the second
commanded turn is a caster's, whether Cast or Attack is actually offered that turn,
whether fight 1 stays clear long enough to reach a Long Rest, and whether a caster's
slots span more than one level are all facts about a fight in progress, not guarantees,
and each is checked by *availability* (`ButtonOffered`, or `castable`'s own index, before
acting) rather than by whether the click's own effect happened to land: the old
`play-7-spell-menu` reported "Cast was not offered" whenever the spell menu failed to
open, and the old attack-menu and slot-menu branches folded a real failure into "no such
character took a turn" and "no such spell exists" respectively — all three conflated an
unreachable turn with a real defect the probe had just found. A capture the probe could
not attempt writes `<name>.skipped.txt` next to where the PNG would have gone, naming
why — so a shrunk capture set is a file to notice rather than a silent absence. This is
reserved for coverage that is genuinely optional; a required step's failed predicate is
the fault above, never this.

```bash
scripts/probe-diff.sh <dirA> <dirB>
```

Runs both invocations above twice, into two directories, and `diff -rq`s the result —
recursively, over every file including the skip markers, not a fixed PNG list — so
"the probe still passes and its captures are unchanged" (every slice of #327 after this
one) is a command with an exit code rather than a promise.

**It works as a gate — it caught a real regression the day it was written.** A full pair
measured 2026-08-26 came back eleven of fifteen PNGs differing, every one of them by a
single small bounding box (`(45×45)` or less) at the active combatant's pulsing ring —
nothing else on a 1920×1080 frame moved a pixel: not a position, not a log line, not a
damage roll, not a menu row. That is `FightScreen.ActiveRingNow` (#494/#512, landed the
same night), which reads a wall-clock tick with no probe/capture guard — filed as #518.
The four captures with no active-turn ring on screen at all (`run-0-interlude`,
`run-9-outcome-card`, `run-9-after-fight`, `run-10-shop`) came back perfectly identical,
which is what pins the cause to the ring and nowhere else: everything RNG-driven or
state-driven in this probe is exactly reproducible, and only the one unguarded clock read
is not. Fixing #518 is not this slice's to do (`client/FightScreen.cs` has another PR
in flight); the point stands regardless of when it lands — a byte-identical comparison
that cannot yet promise "identical" still told the truth about exactly what differed and
why, which is what a gate is for.

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
signature change from breaking the client silently.

**Since 2026-08-26 there is also a test project** (`tests/SRDCombat.Viewer.Tests`, #190),
in the solution so CI runs it. It is a plain xUnit project referencing this one, and it
holds to a boundary worth knowing before adding to it: Godot's *managed* value types
(`Color`, `Vector2`, `Rect2I`, the enums) work anywhere, and a `Node`-derived type's
*static* members can be called, but constructing anything deriving from
`GodotObject`/`RefCounted` — `Image`, `Texture2D`, `InputEventKey` — **terminates the
test host process** rather than throwing something a test can catch. So the tests cover
rules (the log's colouring, the sprite metrics, the draw scale) and the probe loop below
still covers everything that needs a live scene. The gap that remains is the argument
wiring and `PlayMode`'s own state as a live Godot node — nothing in the test project
constructs `PlayMode` or calls `OnReady`. `BattleScenario` (#473) has since landed and
`SRDCombat.Viewer.Tests` now pins the focus stack, the router and the scenario seam, so
what is left is narrower than when this paragraph was written: the argv boundary
(#490) and live-node state, both still probe-only.

Two things the arrangement never cost, and still does not:

- **The build discipline.** `Directory.Build.props` reaches this project anyway — MSBuild
  walks up from the project's own directory — so `TreatWarningsAsErrors`, `Nullable` and
  the analyzers all apply here exactly as they do in `src/`.
- **Building without Godot.** `Godot.NET.Sdk` is a NuGet package, so
  `dotnet build client/SRDCombat.Viewer.csproj` works on a machine that has never seen
  the editor. Only *running* the scene needs Godot itself.

## The rule this client is held to

The same one as the console client: **it holds no rules.** Positions, hit points,
conditions and the narration all come off the engine's public API, every action is one
of the engine's own and every refusal is displayed, never interpreted. The one choice
the client makes — which attack a click means — is a player convenience, not a rule, and
it is shared with the console client (`AttackChoice` in `SRDCombat.Game`) so the two
cannot drift apart on it. Even the movement highlight is the engine's own
`MovementRules.Reachable`, one bounded search for the whole board (#726 — it used to be
`FindPath` asked once per square); the play screen decides only what to colour, and a
route to one chosen square is still `FindPath`'s answer for that destination.

The screens split over one design fact, written on `WatchMode`: `IRandomSource` is
consumed as a fight goes, so scrubbing means resolving once and snapshotting every turn,
while playing means holding the one live `Encounter` and never replaying anything.
