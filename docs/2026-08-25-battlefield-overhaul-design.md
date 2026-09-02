# Design — battlefield generation overhaul

**Date:** 2026-08-25. **Author:** designer. **Mandate:** Brandon's played-run verdict,
verbatim: *"the random battlefield system we have set up sucks, bad. it's so sparse,
doesn't make any sense, boring, the battlefields need more attention in order to be
interesting and actually fun, and not look like kindergarten garbage."* Per protocol a
played-run complaint outranks any measured number; the pacing tables that priced terrain
as "measured, cost nothing" priced the bot's experience, not his.

**Supersedes #243** (density tiers and a central-wall battlefield) — both of its items
are folded in here as slices 1 and 3, with their reasoning kept. **Coordinates with
#429** (multi-square creatures) without blocking on it — see [§8.1](#81-connectivity-is-span-aware).
**Does not touch** the starting separation or board width: #188's finding (widening is
blocked on the level-1 wall) stands, and every shape below is parameterized on the board
it is given, so if #188 later widens the field no site needs rework.

This document is the spec. The implementation slices are filed as issues (listed in
[§12](#12-slices-and-sequencing)); each carries its own acceptance criteria and
measurement gate. Nothing here is implemented yet.

---

## 1. The diagnosis — why the boards are bad, measured

All numbers from a 200-seed survey per level (levels 1 and 3, Moderate, the
pregenerated party — the same fights `--watch` shows), generation code at `e424783`.
The survey instrument was ad hoc (a scratch harness over `EncounterFactory.Build`) —
QC reproduced every figure independently, and the *committed* check becomes S1's
coverage property test, which is the honest resting place for these numbers. The
probe screenshots that accompany the visual critique artifact show the same boards
the numbers describe.

**1. Sixty-one percent of the board is bare by rule.** `TerrainGenerator` places
features only on columns *strictly between the outermost spawn columns* — for the
standard fight, columns 9–19 of 28. The 8-column flanking margins on each side, added
2026-08-21 as "room to manoeuvre", can never hold a single feature. Measured: the
terrain-eligible band is 39% of board width on every one of 400 sampled boards. The
flanks the player is invited to use are a featureless parade ground, and every board
reads as a thin decorated stripe between two empty fields.

**2. Total coverage is ~3%.** Three to six obstacle attempts survive rejection to a
mean of 2.7 footprints ≈ 15 impassable squares of 504 (3.0–3.2% across levels), plus a
mean 3.0 squares of Difficult Terrain (0.6%). A 28×18 board carries two or three props.
That is the sparseness, quantified.

**3. Structure is impossible by construction, not just unlucky.** The generator knows
exactly three shapes — 2×4 wall, 4×2 wall, 2×2 low obstacle — and footprints may never
touch, orthogonally *or* diagonally (the rule exists so the client can recover each
piece as a connected component). So no L-wall, no corner, no room, no doorway, no
corridor, and no wall longer than four squares can ever be generated, at any density,
on any seed. The most tactical shape the system can express is a lone 4×2 block in
open ground. This is why boards "don't make any sense": nothing on them is *for*
anything.

**4. Placement is uniform confetti with no spatial logic.** Anchors are drawn uniformly
over the band × the full height, with no attraction to the contested ground, the lanes
between the sides, or each other. Features routinely land in dead corners behind a
spawn line (seed 10: two of three walls in the far south rows nobody crosses; seed 12:
a wall cluster on the bottom edge). Nothing guarantees a single feature lies on any
path between the sides — a board passes every check with all of its terrain tactically
irrelevant.

**5. One distribution for every fight, and the art is a stranger to the structure.**
No density variation between fights (that was #243's first item), no link to cycle or
level, and no link to the client's three ground themes — the client picks its theme by
hashing the battlefield's square counts, so a woodland board and a clay board are
structurally identical and the theme means nothing. Deployment compounds it: every
group is a straight single-file line — under the Columns draw a nine-monster warband
is one column of nine; CornerGroups is two single-file stacks and Surrounded a fanned
ring, lines all the same — so the opening frame of every fight is queues facing each
other across an empty field.

The one thing the current generator does right, and every slice below preserves:
**every fight stays winnable on foot** — `StaysConnected` admits obstacles
square-by-square and no draw can wall a side off. #256's stall class is closed and
stays closed.

## 2. Design principles

1. **Every board asks a question.** The project's decision test, applied to ground: a
   battlefield is interesting exactly when the approach is a choice with defensible
   alternatives — which door, which ford, around or through, interior or flank. A board
   whose only plan is "walk forward" fails the test no matter how much scatter is on it.
2. **Structures, then dressing.** Coherence comes from generating a *site* first — one
   structural idea per board, placed where the fight will happen — and scattering
   dressing around it. Scatter alone can never look designed, because it isn't.
3. **Legible, not decorative.** Every square keeps exactly the three rule meanings the
   engine already has (wall / low obstacle / difficult). Structures are compositions of
   those meanings, never new rules. What a square does must stay readable at a glance
   — F2's foresight work (#301–#303) depends on it.
4. **Variety includes the plain.** A bare open field stays a possible draw, deliberately
   rare — the same reasoning that keeps Columns the commonest layout.
5. **Fair ground, asymmetric ground.** Neither side may *start* owning the site: the
   primary structure lands on contested ground, not in a deployment zone. Along the
   other axis it may sit off-centre, so boards read asymmetric without granting either
   side the castle. (The old "a difference the fight never earned" doctrine in
   `EncounterFactory` guarded fairness by enforcing symmetry; this keeps the fairness
   and drops the symmetry.)
6. **Deterministic, and rejection never re-times the dice.** All randomness through
   `IRandomSource`; each generator consumes dice in a fixed pattern whether or not a
   draw is accepted — the discipline `TerrainGenerator` already states.

## 3. The architecture: site plan, then dressing

Generation becomes two layers, both seeded from the fight's own dice:

1. **The site plan.** A seeded draw picks one *site type* for the fight (weights in
   [§4](#4-the-site-catalogue)) and the site generator places the board's primary
   structure(s) with stated placement logic. One structural idea per board, so a
   battlefield reads coherently — the same reasoning #243 gave for one density tier
   per fight.
2. **The dressing.** The current scatter pass (obstacle footprints + difficult
   patches), retuned: eligible over the whole board ([§5](#5-the-whole-board-is-in-play)),
   counts set by the fight's density tier ([§6](#6-density-tiers)), and biased toward
   the contested ground rather than uniform.

The site plan owns the board's tactical question; the dressing owns its texture. The
layers are ordered so the dressing can never break the site (a doorway is carved by the
site generator and registered as protected — dressing may not land in or adjacent to a
protected gap).

`EncounterFactory` keeps its current sequencing — layout drawn, spawns placed, then
terrain — with one addition: the site draw happens between layout and terrain, and the
site generator receives the layout, because placement logic is layout-aware
([§4.6](#46-placement-is-layout-aware)).

## 4. The site catalogue

Each site type states its tactical question, its construction, and its placement rule.
All parameters are stated constants with doc comments, per the `AreaTargeting` model;
the numbers below are the design's starting values and the measurement plan
([§11](#11-measurement-plan)) is what validates them.

### 4.1 Open field (the current game, kept)

*Question:* none — this is the palate cleanser. Construction: dressing only, no
structure. At sparse density this is today's board; that draw stays possible on
purpose and becomes the exception rather than the rule.

### 4.2 Central wall (#243's second item, folded in)

*Question:* which gap, and who holds it. A wall run spanning most of the board's height
(or width, for Surrounded), lying in the middle band between the sides, with **one or
two carved gaps** of stated width ([§8.1](#81-connectivity-is-span-aware)). The sides
must route around or through to even see each other; ranged play changes completely,
and when fog-of-war slice 2 (#244) lands, this is where hidden approach actually
happens — #243's own observation, kept. Wall segments are `Blocked` (Total Cover);
gaps are protected squares. A variant draw replaces up to a third of the run with low
obstacle (a ruined, shootable stretch — the wall degrades rather than repeats).

### 4.3 Ruined rooms

*Question:* interior or flank. One to three rectangular room shells (walls with one to
two doorways each), roughly 4×4 to 7×5, allowed to share a wall or stand apart;
interiors may carry a low obstacle or difficult patch. A room is cover, a firing
position, an OA trap, and — with #244 — a dark interior. Shells may be *ruined*: each
wall run has a chance to drop segments, so rooms read as ruins rather than buildings,
which is also what keeps them legible from above without roof art.

### 4.4 Boulder field / grove

*Question:* which lane. Many low-obstacle clusters (and, on woodland, wall-class trees)
in loose groups of one to three footprints, placed to leave two to four distinct clear
lanes between the sides. Cover-rich, line-of-sight poor, the skirmisher's board. This
is the site that most rewards F2's threat marking (#301) — weaving between clusters is
exactly where opportunity attacks live.

### 4.5 Crossing

*Question:* which ford, and who pays the crossing tax. A band of Difficult Terrain
(river, bog, scree — theme decides the art) spanning the board's height, two to four
squares deep, lying in the middle band, with one to two clear fords. Difficult Terrain
is passable, so this site cannot affect connectivity at all — it is the safest
structural site and lands first ([§12](#12-slices-and-sequencing)). Dashing across the
deep bog versus queuing for the ford is a real choice for both the player and, someday,
`ITacticsPolicy` (#314).

### 4.6 Placement is layout-aware

The site's primary structure lands on **contested ground**, defined per layout:

- **Columns:** the structure's centroid falls in the middle third of the x-axis between
  the two spawn columns; free along y (the asymmetry axis — principle 5).
- **CornerGroups:** contested ground is the union of the two approach lanes from the
  corner groups to the party column; the structure's centroid falls inside it. A
  central wall under this layout naturally splits the two approaches.
- **Surrounded:** contested ground is the ring between the party block and the monster
  ring; central-wall and crossing draws re-roll to a different site here (a wall
  through the party's spawn square is not a site, it is a bug), or place as arcs of
  the ring — implementer's choice, stated in the doc comment, with arcs preferred.

Draw weights, all levels (a per-cycle reweighting is F3 material — see
[§13](#13-relation-to-the-rest-of-the-backlog)): open field 30%, boulder/grove 20%,
central wall 20%, rooms 15%, crossing 15%. No level gate on sites: unlike layouts,
terrain is symmetric opportunity, and cover on the approach is the best hypothesis
anyone has offered for *softening* the level-1 wall (the wall's mechanism is free
ranged shots across open ground — #188). The measurement plan tests that hypothesis
rather than assuming it.

## 5. The whole board is in play

The spawn-column band rule is **retired**. New eligibility: terrain may land anywhere
on the board except (a) any spawn square, (b) any square adjacent to a spawn square,
and (c) any protected square (carved gaps, fords) or any square adjacent to one — the
same adjacency §3 states ("dressing may not land in or adjacent to a protected gap"),
not membership alone. Rule (b) is a **new, stronger
guarantee, not a restatement**: today's `InRegion` excludes only spawn *squares*, and
QC measured ~26% of current boards (511 of 2,000 on the replica) with impassable
terrain standing flush against a spawn. S1 genuinely tightens near-spawn placement —
a behaviour change, gated by S1's full-range measurement like everything else — and
§8.1's overlap-suffices argument load-bears on exactly this free 3×3 block, which is
why the clearance is a stated rule here rather than an accident of the band. The doc-comment bullet "terrain sits strictly between the
outermost spawns" is rewritten to say this, in the same commit — docs are part of the
diff.

The dressing pass keeps a mild bias toward contested ground (weighted draw: two-thirds
of dressing anchors from the contested region, one-third from the whole board) so the
flanks gain texture without the middle losing primacy.

## 6. Density tiers

#243's first item, as specced there: a seeded draw picks **sparse / standard /
cluttered** per fight, before anything is placed, as multipliers on the dressing counts
and the site's optional elements. One tier per fight so the battlefield reads
coherently. Target total coverage (impassable + difficult, measured over generated
boards, stated as a validator not a hope):

| Tier | Draw weight | Target coverage | Today, for reference |
| --- | --- | --- | --- |
| Sparse | 25% | 3–6% | ~3.6% always |
| Standard | 50% | 7–11% | — |
| Cluttered | 25% | 12–16% | — |

A property test generates boards across a seed sweep and asserts each tier's realized
**mean** coverage lands in its band (a floor *and* a ceiling — the extraction
chapter's lesson about floors applies here too). Stated plainly: the bands are a
distribution claim over the sweep, not a per-fight guarantee — a single cluttered
warband board can tail below its band under rejection, and a per-board assertion
would fight that rejection forever. #433's acceptance criterion says the same.

## 7. The structure vocabulary, and the shape the model grows

The three fixed footprints and the never-touch rule cannot express a site, so both
change:

- **Shapes:** wall *runs* (1×N and N×1, N from 2 to a stated maximum ~10), corners and
  T-joins (compositions of runs), room shells (runs with carved doorways), low-obstacle
  clusters (2×2s that may abut into organic clumps), difficult bands and patches.
- **The never-touch rule is retired** — it existed so the client could recover each
  piece as a connected component of blocked squares, and it is exactly what forbids
  corners and rooms. Sequencing, pinned so S2 cannot be implemented two ways: the
  rule is retired as a *model* constraint in S2 (site structures and `TerrainPiece`
  stop assuming it), but the **dressing pass keeps its separation behaviour
  unchanged until S4's clusters actually use abutment** — S2's visible board diff is
  nil by construction, which is what its "client renders unchanged boards
  identically" criterion means. Recoverability is replaced by honesty: **the model grows a
  shape.** `Battlefield` gains `IReadOnlyList<TerrainPiece>` — kind (wall run / low
  cluster / difficult region / gap), its squares, and the site type that placed it.
  The square sets (`Blocked`, `LowObstacles`, `DifficultTerrain`) remain the *rules*
  authority — cover, movement, and every engine path read only those, unchanged — and
  the pieces are the *description*, for clients and tests. `Battlefield` is rebuilt
  from the seed on load and never serialized into a save, so no save-format concern
  exists (confirm, don't assume, in the slice).
- The client's component-recovery (`CollectScenery`) already falls back to per-square
  standing sprites for non-rectangular components, so structures render safely from
  day one; rendering them *well* (tiled wall segments, corner pieces) is the client
  slice plus art asks to Brandon ([§10](#10-engine-versus-client-and-the-art-asks)).

## 8. Constraints — what every slice must hold

### 8.1 Connectivity is span-aware

`StaysConnected` generalizes from single-square BFS to *span-aware* connectivity: a
route exists between every pair of spawn squares for a K×K footprint, where K =
max(2, the largest creature span the pool can field) once #429 lands, and K = 2 today
— **every carved gap, ford, and guaranteed corridor is generated at width ≥ 2 from the
first slice**, so multi-square creatures arrive to boards that already fit them. The
parameter is threaded now, cheap, rather than retrofitted later across five site
generators. (Coordinated with the #429 designer and architect, 2026-08-25: their spec's "every
generated encounter places all footprints legally" acceptance criterion and this check
are the same check; whichever lands first, the other reuses it. #429 fields 3×3 via
the Awakened Tree, so K's derivation must come from the pool — the amended #429
criterion 7's `MonsterPool.LargestSpan(...)`, called with the same `Draw` flags the
encounter's actual draw uses — never a constant.)

The agreed shared shape (confirmed with architect-429, 2026-08-25):
`GridConnectivity.StaysConnected(impassable, candidates, anchors, width, height,
spanSquares)`, preferably beside `GridPosition` in `Core.Combat`, reducing
byte-identically to today's check at span 1. Semantics, pinned: every anchor *square*
must be overlapped by a footprint position belonging to one shared connected component
of the eroded free space (positions where a K×K footprint fits). Anchors stay 1×1
squares — requiring them to be valid K×K anchor positions would fail spuriously near
board edges. Overlap suffices *because* of §5's spawn-clearance rule (a free 3×3 block
around every spawn); that coupling is stated in the doc comment, and weakening the
clearance rule means relaxing the check to overlap-or-adjacent in the same commit.

One print fact makes the width guarantee load-bearing rather than cosmetic: **SRD
5.2.1 has no squeezing rule** (verified against the PDF by the #429 design — the word
appears nowhere), so a gap narrower than a creature's footprint is a hard wall to it,
not a slow passage. A carved gap is a route only for the spans that fit it.

Square-by-square admission stays for dressing; site structures are admitted
whole-or-nothing with the span-aware check run once per structure (a room that cannot
stand leaves no half-room).

### 8.2 Everything else

- **Every objective completable.** Last-side-standing and KillLeader reduce to
  connectivity (covered above); SurviveRounds must not become trivially safe — a
  survive rung on a cluttered rooms board is a kiting paradise, so the measurement
  plan watches `Survive` rung outcomes specifically, and #311's redesign of the free
  Survive(3) rung should land with or before the rooms site.
- **6–10 warbands fit.** Spawn zones are placed before the site and are inviolate;
  the site generator receives them and rejects rather than displaces. Board height
  already grows with side size; nothing changes there.
- **Determinism.** All draws through `IRandomSource` in fixed consumption patterns;
  a rejected structure consumes the same dice as an accepted one.
- **No stall regressions.** A slice must not make fights unresolvable — this is the one
  severe risk terrain work carries, and it stays a hard gate on every slice (revised
  2026-08-28: shown directly, against the stall class's own tests and
  `GridConnectivity`'s invariants, rather than by sweeping the canonical ranges; the
  ranges confirm it at the re-baselining checkpoint). Denser terrain re-rolls every
  fight's dice and will expose latent shapes the way the denser-terrain branch exposed
  #256 — that is the gate working, not a reason to stay sparse.
- **Performance.** #328 (504 pathfinds per action) predates this and gets worse with
  more blocked squares only marginally; the span-aware BFS runs at generation time
  only. No new per-action cost.

## 9. Deployment: zones and formations

Single-file columns are retired. Each side gets a **deployment zone** (the column
becomes a 2–3 deep block region); the party fills it as a 2-deep formation, monsters
fill theirs **grouped by kind** (three goblins stand together; the ogre anchors a
flank), with the seeded dice choosing among valid arrangements. Layout semantics are
unchanged — Columns/CornerGroups/Surrounded keep their geometry and gates; only the
shape of each group changes. The nearest-rank separation (60 ft / 30 ft surrounded) is
preserved exactly: formations grow *away* from the enemy, never toward, so no fight
gets closer than today's and the #188 finding is not disturbed.

## 10. Engine versus client, and the art asks

**Engine (generation, `SRDCombat.Game` + the `Battlefield` model):** site draw, site
generators, density tiers, whole-board eligibility, span-aware connectivity,
`TerrainPiece`, deployment zones, and a `BattlefieldTheme` chosen at generation —
woodland / rock / clay to start, matching the three ground themes Brandon has drawn —
carried on the battlefield so structure and art agree (a crossing on woodland is a
river; on clay it is a scree bank). The client's hash-pick is replaced by reading the
theme; sites may weight themes (grove → woodland) without hard-binding them.

**The theme draw's stream discipline (QC finding, 2026-08-25):** within one fight,
everything draws from *one* seeded stream in order — terrain is followed on that same
stream by `Encounter.Start` → `RollInitiative` and every combat roll after it, so a
theme draw placed "after all terrain" still re-times initiative and the whole fight
on every seed. The theme must therefore consume **zero dice from the fight stream**:
derive it arithmetically from the fight's identity (`RunDice`-style, e.g. from the
seed and fight number), or from a separately derived stream. Anything that rolls the
fight's own dice for it is a full-population balance change and pays the full
two-range measurement gate. #439's acceptance criteria say the same.

**Client (`client/`, rendering only, no rules):** render `TerrainPiece` instead of
recovering components; tile wall runs from segment art; draw difficult bands as
regions rather than per-square smears where the theme has the art. Falls back to
today's rendering wherever art is missing — a theme without a drawing keeps the flat
colours, the established pattern.

**Art asks to Brandon (filed as asks, never generated):**

1. Wall-run segments per theme: straight, corner, end-cap (the current pillar art
   covers the lone-block case already).
2. Difficult-terrain *band* art per theme (water/ford for woodland, bog or scree
   variants elsewhere) — today's brambles cover patches.
3. Optional, later: door/gap framing, interior floor variant for rooms.

Nothing blocks on the art: every piece renders with existing art or flat colours until
a batch lands with his before/after approval.

## 11. Measurement plan

**Superseded 2026-08-28, Brandon's direction: per-slice pacing sweeps are retired.**
This section originally required every slice to quote
`tools/PacingMeasure -- --seeds 1-120` and `200-320` against a same-build baseline, and
said the spot-check waiver never applied here (terrain sits on the fight's one seeded
stream ahead of initiative, so any change re-times every fight on every seed — which is
still true, and still the reason a *checkpoint* sweep must include this work). What
changed is the cadence, not the physics: S3–S7 land without sweeps, and their combined
effect is read at the next re-baselining checkpoint (#542). See CLAUDE.md, Standing
conventions. Terrain remains a balance change (#243's standing warning: the two
"cosmetic" plausibility fixes cost as much pacing as potions bought) — which is why the
checkpoint matters, not why every PR should measure.

- **The one severe risk, checked per slice and directly:** a slice must not make fights
  unresolvable. `Stalled` stays zero and `ended:` stays defeat/victory-shaped; a rise in
  round-limit endings is a red flag on any site, cluttered boards especially. This is
  named-risk work — the stall class has its own tests and `GridConnectivity` its own
  invariants — not a reason to sweep the canonical ranges.
- **Hypotheses for the checkpoint, direction stated per slice** (predictions to test
  when the sweep runs, not per-PR acceptance criteria): cover-rich sites should *reduce*
  died-by-fight-4 (the free-ranged-shots mechanism runs both ways); central wall
  should lengthen fights (per-fight rounds, if the instrument reports them, else
  read per-band hp-left); crossing should be near-neutral.
- **Watched:** `Survive` rung win rates once sites land (§8.2); per-band hp-left for
  drift outside noise.
- **The bot's numbers are a floor.** The placeholder policy does not use cover, does
  not hold doors, and does not kite fords; the tension is designed for the human.
  Regressions in the numbers are real; improvements in fun are Brandon's to judge —
  a played-run report accompanies the phase exit, per F3's own rule.

## 12. Slices and sequencing

Filed as issues, one concern each, in dependency order: S1 #433, S2 #435, S3 #436,
S4 #437, S5 #438, S6 #439, S7 #440. Phase: **F2-feel** for slices
1–7 — Brandon's verdict is a feel complaint about what is on screen, this work is what
F2 exists for, and it pairs with F2's foresight work (threat marking and path preview
mean more on boards worth previewing). The per-cycle site weighting and any
run-structure coupling stay F3.

1. **Whole-board eligibility + contested-ground bias + density tiers.** Retires the
   band rule; supersedes #243 item 1. Biggest visible win per line of code.
2. **Structure vocabulary + `TerrainPiece` + span-aware connectivity.** The model
   change everything after depends on; no new sites yet, so the visible diff is small.
3. **Sites: crossing + central wall.** Crossing first within the slice (connectivity-
   safe), then the wall (#243 item 2). First boards that ask a question.
4. **Sites: boulder field/grove + ruined rooms.** Rooms carry the Survive-rung watch.
   Use the stated 15% rooms weight. If the Survive-rung concern surfaces, stop for
   Brandon's decision; do not change the weight or reopen #311 as part of this slice.
5. **Deployment zones and formations.**
6. **`BattlefieldTheme` on the engine; client reads it.**
7. **Client: render `TerrainPiece` structures; file the art asks.** (art-tech +
   Brandon's approval loop.)

Each slice lands green on the full gate before the next starts; slices 3 and 4 each
carry their own pacing quote. The property tests (coverage bands per tier, span-aware
connectivity sweep, every-encounter-resolves on a seed sample) land inside slices 1–4,
not as a trailing task.

## 13. Relation to the rest of the backlog

- **#243** — superseded; both items live here (§6, §4.2). Close on the first slice's
  merge with a pointer.
- **#188** (wider battlefield, F4) — untouched; all sites parameterize on board
  dimensions. If widening lands, sites stretch, gaps stay gap-width.
- **#429/#430** (multi-square creatures, F4) — §8.1's K parameter is the contract;
  neither blocks the other.
- **#244** (fog slice 2, F4) — central wall and rooms are where LOS-fog earns its
  keep; land sites first, fog reads them for free.
- **#301–#303** (F2 foresight) — threat marking, range preview and path preview all
  become more valuable on structured boards; no code dependency either way.
- **#192 / per-cycle variety (F3)** — per-cycle site weights (cycle 3 favors rooms,
  cycle 5 crossings, or whatever the run design wants) is the F3 follow-on, filed
  when F3's route-choice design lands, not now.
- **#311** (Survive rung, F3) — should land with or before the rooms site (§8.2).
- **#328** (client performance, F2) — unaffected at generation time; noted in §8.2.

## 14. Judgement calls reserved for Brandon

Listed at the top of the visual critique artifact, restated here for the record:

1. **The site catalogue's fiction.** Ruins, rivers, boulder fields — which families
   belong in this game's world, and which are missing (a camp? a bridge?).
2. **Density ceiling.** Do the cluttered mocks read as rich or as cluttered noise —
   is 12–16% the right ceiling?
3. **Asymmetry taste.** Structures off-centre along the free axis: interesting, or
   unfair-looking?
4. **The bare field.** Keep it as a rare draw (the design says yes), or retire it?
5. **Theme ownership.** Engine picks the theme per fight (the design says yes) —
   or does he want theme tied to the run's cycle instead (a cycle in the woods)?
6. **The art asks** in §10 — what he wants to draw, in what order; nothing blocks
   on any of it.
