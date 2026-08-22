# Project review — 2026-08-21

A four-viewpoint audit of the whole tree at PR #282 (architecture, game design,
client/UX/aesthetics, content pipeline and rules fidelity), run independently of the
project's own docs and verified against them. The finishing plan in `CLAUDE.md` is
built from this document; each finding below is the evidence for a plan item.

Verified before writing: **972 tests passing, 1 skipped by design, 0 failures**; Debug
and Release both build with 0 warnings; the status table's numbers in `CLAUDE.md`
(spell counts, pacing figures, pool sizes) all reproduced under audit. Self-reported
status in this repo is accurate — worth saying, because that is what makes the rest of
this document short: the docs can be trusted, so only the *gaps* are listed.

## The one-paragraph verdict

An unusually well-engineered project with an inverted investment profile. The combat
engine, the rules scholarship and the measurement practice are better than most
commercial tactics games. The two layers a player touches — the game *around* the
fights and the presentation *of* the fights — are far behind the engine. The expensive
part is done; what remains is making it felt.

## Architecture

**Strengths.** `Core`'s purity is compiler-enforced (a zero-reference csproj; zero
I/O, ambient randomness or clock calls in 15,218 lines, verified by grep). The frozen
transcript is an instrument, not a snapshot — it asserts the pinned fight still
exercises the hard interactions. Refusals are values with stable codes; zero blanket
catches, zero warning suppressions, zero TODOs. The save persists choices only and
re-derives everything on load, so a hand-edited save cannot cheat.

**Risks, ranked.**

1. **`Encounter` has no extension seam.** 3,983 lines over five partials, ~20 public
   action methods (one per game concept), zero polymorphism, and a ~4-check guard
   preamble copied ~16 times. Every new feature widens the same class.
2. **41% of production code (14,690 lines) has no test project**: `client/` (7,213),
   `tools/SrdExtract` (5,377), `src/SRDCombat.Console` (1,808 — untested *and*
   unfiled; #189/#190 name only the first two), `tools/PacingMeasure` (292). The
   extractor's parsers — the source of truth for every number in the game — have zero
   direct tests; only their committed output is validated.
3. **The save write is not atomic.** Bare `File.WriteAllText` in both clients
   (`Program.cs:184`, `PlayMode.cs:475`), no temp-and-rename, no backup. A crash
   mid-write destroys a run. ~10 lines to fix.
4. **Save-vs-content drift crashes instead of refusing.** No content version in the
   save; raw dictionary indexers in `PregeneratedParty.cs:108-110` (also
   `Gauntlet.cs:424`, `Loot.cs:99-101`, `Shop.cs:359`) throw `KeyNotFoundException`
   past both clients' exception filters.
5. **The suite takes ~8 minutes**, 7m22s of it `Game.Tests` (191 tests) simulating
   whole 30-fight gauntlets to assert single facts and loading the content corpus 27
   separate times (zero xUnit fixtures).
6. **One interface in 35,743 production lines.** `SimpleTacticsPolicy` is static, so
   two AIs cannot be A/B'd on the same seeds — a seam the measurement method itself
   wants.

Smaller: `CreateMode : FightScreen` (inheritance for palette reuse, the name lies);
three `async void` probes whose exceptions vanish; `TurnResources` mutators public for
want of `InternalsVisibleTo`; `RepositoryPaths` triplicated across test projects;
`ProjectReferenceTests` is a stale placeholder.

## Content pipeline and rules fidelity

**Strengths.** The monster accounting is load-bearing: 1,020 counted unmodelled
clauses *drive* `MonsterPool` grading and `ConditionRules.CanBeImposed`. The
extraction is genuinely reproducible (28 of 29 data commits also touch `tools/`; one
self-invalidating `KnownCorrections` entry). Serialization guards hold. The MIT/CC-BY
licensing split is done properly and argued in writing. Spot-checked 2024 rules —
cover degrees, death saves, Petrified's whole page, Grappled's
anyone-but-the-grappler, Stunned's deliberate lack of Speed-0 — are right, with
judgement calls written down.

**The honesty rule is broken in three places, and one is Bug 1's fourth occurrence:**

1. **Multiattack accounting is a live silent-loss lane.**
   `EntryMechanicsParser.MatchesStructuredForm` returns `true` for any Multiattack
   unconditionally, so 45 entries claim full modelling while dropping "It can replace
   one attack with a use of Roar/Spellcasting/…". Eight are at CR ≤ 4 — the Lion
   grades `Playable` and is *in the pool*; the Pirate grades `Complete`.
2. **Species traits execute nothing and the player is not told.** All 33 traits
   contribute zero mechanics (species is name + size + speed), yet both creation
   flows print each trait's full text with no caveat, and
   `CharacterSheet.UnimplementedFeatures` covers class features only.
3. **Spell-level `UnmodelledClauses` is structurally never populated**
   (`SpellParser.cs:321` empties it for any structured spell), so
   `SpellDefinition.IsFullyModelled` returns true for Web and Cloudkill. Harmless
   only because nothing reads it; `PreparableSpells` is the sixth allowlist working
   around it.

**One undocumented rules divergence:** concentration never breaks on Incapacitated —
`CheckConcentration` is reachable only from the damage paths, so a caster paralyzed,
stunned or petrified by a save keeps concentrating. Unlike every other gap, this one
is written down nowhere.

**Licensing gaps, minor:** no CC-BY attribution shown anywhere in the *running* game
(NOTICE.md covers the repo, not the built artifact); `client/assets/masters/` falls
under MIT by omission without that being stated anywhere.

**Doc drift found:** `Gauntlet.cs:106-114` describes the old cycle (2 Low / 2
Moderate / 1 High, 350 XP) where the code builds 3/1/1 = 325 — and that arithmetic
justifies the ladder's length; `WeaponMasteryRules` claims 33 of 38 weapons, the data
says 30; `ContentSerializer.cs:28` cites a `ContentShapeTests` that is actually
`ContentSerializerTests`; `MonsterTraitRegistry`'s closing note predates #28;
`SimpleTacticsPolicy`'s header describes its predecessor; `client/README.md` has four
factual drifts (4 vs 7 ground tiles, 3 vs 1 tiles-per-square, the dragon-colour note,
"five animations play" — no shipped asset has more than one frame per pose).

## Game design

**Strengths.** Measurement as method, instrument committed, claims carrying seed
range + baseline + date, and negative results reverted. The "XP prices worth, not
simultaneity" insight is real and quantified. The tactical layer — cover along the
line of fire, OA-aware pathing, layouts, fog, warbands, Weapon Mastery — is a real
game. Human play treated as a first-class instrument.

**Gaps, ranked.**

1. **The failure loop has no teeth.** The save does not carry the seed and the seed
   defaults to `Random.Shared.Next()`, so `--continue` after a defeat *re-rolls the
   entire remaining ladder* rather than retrying the fight that killed you. No
   ironman, no attempt count, no score; mid-fight quit is free. Every difficulty
   number describes conditions nobody plays under.
2. **There is no between-fight game.** Loot rolled uniformly and auto-equipped;
   level-ups auto-resolve; rests fire on `index % 5`; the shop sells only strict
   upgrades (no trade-off, no decision — which is why `AutoBuy` plays it optimally);
   the ladder is fixed with no route choice. And a live bug: **a player-created party
   silently forfeits its level-4 ASI** — no creation flow ever asks for the
   improvement plan; only the pregens hardcode theirs — so created parties are
   measurably weaker than the parties the balance was tuned on.
3. **Variety is thinner than the pool suggests.** 81 of 330 creatures survive the
   filters; the CR 4 band the boss rung draws from six times a run holds **three**
   stat blocks; **zero spellcasting enemies** are in the pool (all ten CR ≤ 4 casters
   are filtered out), so thirty fights contain no enemy control, healing or buffs;
   the `Playable` grade reads only Action entries (#231), so no goblin ever Nimble
   Escapes; `ClassicMonsterWeight = 3` now double-penalises the 14 genre-appropriate
   Beasts that survived the `TraditionalFoes` cut, starving WildPack and
   DungeonVermin. The distinct-creature measurement predates the cut.
4. **The AI caps everything.** One-ply scorer; never Dodges, Dashes, Disengages,
   retreats, or checks damage immunity (#224 — a Longsword into a Slashing-immune
   Ochre Jelly for fifty rounds with a Piercing javelin on the belt); stalls remain
   (#256). The same policy plays both sides, so every pacing figure is a floor.
5. **The top of the progression is unreachable and the loot inert.** 57 of 120 runs
   reach level 4; level 5 (Extra Attack, Fireball) arrives in the final fights if at
   all. All 13 executed magic items are pure stat modifiers — none changes a decision
   on a turn. The `Survive(3)` rung pays full XP/gold for three rounds of not dying —
   the optimal human play is to kite and collect, six times a run, and the bot can't
   find the exploit so the instrument never reports it. The measured band curve is a
   sawtooth (83→76→68→73→67→69), not a monotone.

## Client, UX and aesthetics

**Strengths.** The refusal contract end to end; log/animation synchronisation
(delayed, never reordered); keys bound to actions not slots; Esc as a cascade ending
in a confirm that states its cost; a camera that frames everyone and keeps the square
under the pointer under the pointer; fog withheld consistently at six call sites; the
projectile system (per-weapon art as a dropped file, never a code change).

**Gaps, ranked.**

1. **The drawn art is not pixel art, and PR #238's fix was reverted rather than
   resolved — now at 15× the scale.** Exactly one sprite (the Barbarian, 31 colours)
   conforms to the project's own 52-entry master palette; every other drawn sprite is
   0% conformant at 800–6,200 colours — photographic downscales that non-integer
   nearest-neighbour resampling turns into fresh noise at every zoom, exactly as
   #238's commit diagnosed. The three terrain themes are effectively three different
   media (`Wall_Rocky` 52 colours vs `Wall_Woodland` 4,121). No pipeline script
   exists anywhere in the repo; every sprite was produced by hand, differently.
2. **Nothing on the board animates and nothing gives feedback.** All 49 monster
   folders hold exactly one file (`Idle.png`); a kill rotates it 90°. **No audio of
   any kind** (zero files, zero players). No damage numbers on the board, no hit
   flash, no shake — a hit and a miss are visually and audibly identical, with the
   number in 12px text on the far side of the screen. With a 0.75s minimum swing and
   0.6s turn beat, the pacing reads as dead air.
3. **Half the roster is lettered circles while 23 finished masters sit unshipped.**
   `client/assets/masters/` holds full-resolution finished drawings for skeleton,
   zombie, ogre zombie, cultist, goblin minion, guard, hobgoblin captain, kobold
   warrior, pirate, worg, warrior infantry/veteran and more — exactly the creatures
   rendering as circles — waiting only on a downscale step. 13 pool names still map
   to gitignored Craftpix folders that can never ship; 8 of 12 classes render as a
   circle under `--create`; five committed drawings (Ape, both Bears, Archelon,
   Giant Eagle) can never appear because `TraditionalFoes` excludes them.
4. **No tactical foresight.** No threat/OA display (the game's central positioning
   rule is invisible until it fires), no hit chance, no AoE or path preview, no undo;
   a misclicked attack resolves instantly and can auto-end the turn. The two advisory
   layers that exist are the least visible things on screen (0.16-alpha movement wash;
   red ring on a red token). Tooltips need a 2-second hover and say nothing for empty
   squares. The initiative panel squeezes the log exactly when fights are busiest.
5. **Structure and scale.** `PlayMode.cs` 2,570 lines / 39 fields holding fight, run,
   shop, three modal cards, four targeting modes and the probe;
   `_UnhandledInput` is a 233-line hand-rolled modal-focus stack; zero client tests.
   Every font size is a hardcoded pixel constant (unreadable at 4K), the typeface is
   Godot's fallback sans under hand-painted sprites. **The size clamp inverts
   creature size**: the Ogre and Ettin render shorter than a Goblin because their
   wide canvases floor the scale to 0.5×. `RefreshAfterAction` runs 504 pathfinds +
   full-board LOS + a fog texture upload after every action by anyone. 291 MB of
   masters committed with no LFS (`.git` is 324 MB). Filename typos (`orge.jpeg`,
   `hobgoglin.jpeg`, `bugear_stalker.jpeg`).

Also: the console client shows no fog (the two clients now give materially different
information), its column ruler is `x % 10` on a 28-wide board, and its terrain legend
exists only in a source comment.

## What this review changes

The plan in `CLAUDE.md` sequences the fixes: integrity first (cheap, compounding),
feel second (the largest gap per hour), the run-as-a-game third (the largest design
gap), depth/variety fourth, test confidence fifth, shipping last. Each finding above
should exist as a GitHub issue before its phase begins — the issue queue remains the
work queue.
