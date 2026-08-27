using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// One authored fight: who fights it, what they fight, and what wins it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A scenario is a value, not a screen</b> (<c>docs/2026-08-26-battle-builder-design.md</c>
/// §3). The builder UI is one author of this value, <c>--spawn</c> capture will be
/// another, and the headless batch runner and the play-one-by-hand path both consume the
/// value rather than either author. That is the whole reason this type lives in
/// <c>SRDCombat.Game</c> beside <see cref="SavedRun"/> and <see cref="ScenarioArguments"/>
/// rather than on a Godot node: every decision about what a scenario <em>means</em> has to
/// be somewhere a test can reach.
/// </para>
/// <para>
/// <b>Drafts, never resolved sheets — the same rule <see cref="SavedRun"/> states.</b>
/// <c>CharacterResolver</c> computes every number on a sheet, so a scenario that stored
/// sheets would store values that could drift from the rules that make them. Gear needs no
/// separate axis for the same reason: <c>WeaponIds</c>, <c>WeaponMasteryIds</c>,
/// <c>ArmorId</c>, <c>HasShield</c> and <c>MagicItems</c> are already draft fields.
/// </para>
/// <para>
/// <b>Nothing resolved is stored, and that goes for the fight too.</b>
/// <c>EncounterObjective</c> has a private constructor and get-only properties, so a
/// scenario stores an <see cref="ObjectiveSpec"/> — the specification the ladder already
/// carries — and never the resolved objective. The battlefield is not stored at all: the
/// overhaul design pins that a board is rebuilt from the seed, so when battlefield
/// overrides arrive (S6, #478) they arrive as a nullable block of <em>specifications</em>.
/// </para>
/// <para>
/// <b>Every data property here has an <c>init</c> accessor, and that is load-bearing
/// rather than stylistic.</b> <see cref="ContentSerializer"/> sets
/// <c>IgnoreReadOnlyProperties = true</c>, so a get-only property is silently not written
/// and silently absent on read — the exact silent-loss shape this project's rules exist
/// for. <c>BattleScenarioShapeTests</c> walks this type's whole property graph by
/// reflection and fails on the first property that cannot round-trip, so a field added
/// get-only next year fails a test rather than disappearing.
/// </para>
/// <para>
/// <b>Why <c>required</c> properties rather than a positional record.</b>
/// <see cref="SavedMember"/> is positional, and System.Text.Json binds a positional
/// record through its constructor — which means a missing JSON member silently becomes
/// the parameter's default instead of an error. <c>required</c> properties are enforced
/// by the serializer: an absent one throws naming the property. Every mandatory field on
/// a scenario is therefore a <c>required</c> property, and the optional ones are nullable
/// on purpose.
/// </para>
/// </remarks>
public sealed record BattleScenario
{
    /// <summary>The party level band a scenario may ask for, and the one the whole game runs in.</summary>
    /// <remarks>
    /// The authority, deliberately: <see cref="ScenarioArguments"/> — the CLI adapter onto
    /// this type (#491) — forwards to these rather than keeping its own copy, so the band
    /// is stated once on the value every author produces instead of once per author.
    /// </remarks>
    public const int MinimumLevel = 1;

    /// <inheritdoc cref="MinimumLevel"/>
    public const int MaximumLevel = 5;

    /// <summary>
    /// Bumped when the scenario format changes incompatibly, and refused rather than
    /// guessed at — the same gate <see cref="SavedRun.FormatVersion"/> is, and the same
    /// rule for adding a field: a new field a scenario written before it existed can
    /// honestly do without is nullable or defaulted and its absence is never refused,
    /// while a field with no honest thing to do about its absence is a format break.
    /// The battlefield block (S6) and per-member starting state (S8) arrive under the
    /// first half of that rule, not the second.
    /// </summary>
    public required int FormatVersion { get; init; }

    /// <summary>A short label — the batch report's header and the library's list.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// What this scenario exists to show.
    /// </summary>
    /// <remarks>
    /// Not decoration. The committed <c>scenarios/</c> directory admits a file on one
    /// rule — <b>if you would cite it in an issue, commit it; otherwise use
    /// <c>--spawn</c></b> — and this field is what makes that rule enforceable rather
    /// than a matter of taste: <c>ScenarioLibraryTests</c> fails a committed scenario
    /// whose notes are blank, naming the file. Required in the JSON so an author cannot
    /// forget the field exists, but permitted to be empty in the type, because a
    /// scenario captured from a live fight onto somebody's own disk (S9, #481) is not a
    /// library entry and owes the library nothing.
    /// </remarks>
    public required string Notes { get; init; }

    /// <summary>Who fights it.</summary>
    public required ScenarioParty Party { get; init; }

    /// <summary>What they fight.</summary>
    public required ScenarioEnemies Enemies { get; init; }

    /// <summary>What wins it, or null for last-side-standing.</summary>
    public ObjectiveSpec? Objective { get; init; }

    /// <summary>
    /// The <see cref="SrdContent.ContentFingerprint"/> this scenario was authored
    /// against. <b>Provenance, not identity.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A deliberate, stated divergence from <see cref="GauntletRun.Resume"/>, which
    /// refuses a fingerprint mismatch outright. A run in progress whose numbers shift
    /// underneath it is a corrupted game, so refusing is right there. A scenario is a
    /// <em>question asked of the current build</em>, and refusing the whole library
    /// after every extractor regeneration would make the surface useless inside a week
    /// — so a mismatch is reported as a notice by
    /// <see cref="ScenarioContent.CheckAgainst"/> and nothing more.
    /// </para>
    /// <para>
    /// What actually refuses is the per-id checking in the same method: a scenario
    /// naming a monster or a weapon this build does not have fails loudly, by name. And
    /// for a scenario <em>inside</em> the repo there is a better guard than either,
    /// because a committed scenario sits next to the content it names: a regeneration
    /// that invalidates one fails CI in the pull request that caused it
    /// (<c>ScenarioLibraryTests</c>), rather than being discovered months later by
    /// somebody opening the file. A save on a player's disk can never be re-checked
    /// that way, which is the whole reason #287 stamps a version and <c>Resume</c>
    /// refuses.
    /// </para>
    /// <para>
    /// Nullable because a scenario is allowed not to say. Absent means "no provenance
    /// recorded", never "matches".
    /// </para>
    /// </remarks>
    public string? ContentVersion { get; init; }

    /// <summary>
    /// The seed a capture came from — a bookmark, nothing more.
    /// </summary>
    /// <remarks>
    /// <b>Never used implicitly by anything.</b> A batch runs a scenario over a range of
    /// seeds and a play-by-hand run takes the seed it was given; if this value were
    /// quietly adopted as a default, a batch's numbers would be about a different fight
    /// than the one the caller asked for. It exists so "the fight that went wrong" can
    /// be found again, and it is read by a human.
    /// </remarks>
    public int? Seed { get; init; }
}

/// <summary>
/// Who fights: the pregenerated four at a level, or an explicit list of members.
/// </summary>
/// <remarks>
/// <b>Exactly one of the two, refused at load if neither or both.</b> The preset case
/// exists so a scenario can say "the pregenerated four" without forking their drafts —
/// otherwise every scenario in the library freezes a copy of <see cref="PregeneratedParty"/>
/// and a change there silently stops applying to any of them. There is deliberately no
/// preset <em>enum</em>: the SRD gives this game one pregenerated party, and a one-valued
/// enum is a speculative abstraction. A second preset becomes a second field, or a
/// discriminator, on the day there is a second preset.
/// </remarks>
public sealed record ScenarioParty
{
    /// <summary>The most members an explicit party may name.</summary>
    /// <remarks>
    /// Twice the pregenerated four — room to test an oversized party on purpose without
    /// admitting a typo's order of magnitude, the same reasoning
    /// <see cref="RosterParser.MaximumCount"/> states for a roster entry. A stated bound
    /// that refuses, never a clamp.
    /// </remarks>
    public const int MaximumMembers = 8;

    /// <summary>
    /// The level to build <see cref="PregeneratedParty.Build"/>'s four at, or null if
    /// this scenario names its members explicitly.
    /// </summary>
    public int? PregeneratedLevel { get; init; }

    /// <summary>
    /// The members, each a draft and the level to resolve it at, or null if this
    /// scenario uses the pregenerated preset.
    /// </summary>
    public IReadOnlyList<ScenarioMember>? Members { get; init; }
}

/// <summary>One authored party member: the choices, and the level to resolve them at.</summary>
/// <remarks>
/// The level is carried beside the draft rather than read off
/// <see cref="CharacterDraft.Level"/> because that is how the rest of this codebase
/// resolves a character — <see cref="PregeneratedParty.Resolve"/> takes a level and
/// applies it with <c>draft with { Level = level }</c>, since levelling is re-resolving a
/// draft at a new level. A scenario that stored only the draft would be asking every
/// consumer to remember which of the two numbers wins.
/// </remarks>
public sealed record ScenarioMember
{
    /// <summary>The choices. Everything derived is resolved from them, never stored.</summary>
    public required CharacterDraft Draft { get; init; }

    /// <summary>The level to resolve the draft at.</summary>
    public required int Level { get; init; }
}

/// <summary>
/// What they fight: an explicit cast, or a budgeted draw.
/// </summary>
/// <remarks>
/// <b>Exactly one of the two, refused at load if neither or both.</b> These are two
/// different questions, and the second is the one the surface earns its keep on
/// (design §2): an explicit roster asks <em>this fight</em>, and a budgeted draw asks
/// <em>this kind of fight</em> — "a Moderate fight for a level 3 party, drawn 120
/// different ways" — which cannot be asked of a fixed cast at all.
/// </remarks>
public sealed record ScenarioEnemies
{
    /// <summary>The explicit cast, or null if this scenario draws to a budget.</summary>
    public IReadOnlyList<ScenarioRosterEntry>? Roster { get; init; }

    /// <summary>The budgeted draw, or null if this scenario names an explicit cast.</summary>
    public ScenarioBudget? Budget { get; init; }
}

/// <summary>
/// One entry of an explicit cast: a monster id and how many of it.
/// </summary>
/// <remarks>
/// <b>Ids and counts, never free text.</b> <see cref="RosterParser"/>'s grammar is for
/// authoring a scenario <em>from</em> a typed line; once authored, the value names content
/// by id, so a scenario cannot silently mean a different creature because a printed name
/// moved. The count shares <see cref="RosterParser.MaximumCount"/>'s ceiling, and for the
/// same reason: a typo must not be able to ask the engine for a two-hundred-monster board.
/// </remarks>
public sealed record ScenarioRosterEntry
{
    /// <summary>The monster's definition id — <c>monster.ogre</c>.</summary>
    public required string MonsterId { get; init; }

    /// <summary>How many of it, 1 to <see cref="RosterParser.MaximumCount"/>.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// A budgeted draw: the printed budget's inputs, and the four axes
/// <see cref="MonsterPool.Draw"/> cuts the bag on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exposing the pool's axes is not bending the allowlist.</b> The cuts stay exactly
/// what they are for the shipped game; a scenario gets to ask what happens on the other
/// side of them — "what does the ladder look like if casters are admitted" (#312) is an
/// F4 question with no instrument today. The price of asking is that the answer must be
/// labelled as what it is: a batch report (S3, #475) states which of these were moved,
/// because a number about a fight the game never generates must never be quoted as a
/// number about the game.
/// </para>
/// <para>
/// Every default here is <see cref="MonsterPool.Draw"/>'s and
/// <see cref="EncounterFactory.Build"/>'s own, so a scenario that says nothing draws the
/// bag the ladder draws.
/// </para>
/// </remarks>
public sealed record ScenarioBudget
{
    /// <summary>How hard the fight should be, priced by the printed page-202 table.</summary>
    public required EncounterDifficulty Difficulty { get; init; }

    /// <summary>
    /// The party level the budget prices against.
    /// </summary>
    /// <remarks>
    /// <b>Not necessarily the party's own level, and a runner must not substitute it.</b>
    /// "A Moderate fight for a level 3 party, fought by a level 1 party" is a legitimate
    /// thing to want to author and the reason this is a field rather than something
    /// derived. <see cref="EncounterFactory.Build"/> today prices against each member's
    /// own sheet level, so the runner that consumes this (S2, #474) has to carry the
    /// value down to <c>EncounterBuilder.ForLevels</c> rather than letting
    /// <c>Build</c> read the party — reading the party instead would be a silent lie
    /// about which fight was measured.
    /// </remarks>
    public required int Level { get; init; }

    /// <summary>The CR ceiling on what may go in the bag.</summary>
    public decimal MaximumChallengeRating { get; init; } = 4m;

    /// <summary>Many cheap creatures on the same budget. Ignored below level 3, as on the ladder.</summary>
    public bool Horde { get; init; }

    /// <summary>The coverage floor a creature must clear to be admitted.</summary>
    public MonsterCoverage CoverageFloor { get; init; } = MonsterCoverage.Playable;

    /// <summary>The pool's plausibility cut.</summary>
    public bool PlausibleFoesOnly { get; init; } = true;

    /// <summary>The pool's genre cut.</summary>
    public bool TraditionalFoesOnly { get; init; } = true;
}
