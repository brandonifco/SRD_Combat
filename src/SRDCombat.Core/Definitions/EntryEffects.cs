using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Definitions;

/// <summary>
/// What kind of mechanics a stat block entry carries.
/// </summary>
/// <remarks>
/// <para>
/// Every entry gets one of these. The point is that an entry can never simply be a blob
/// of text the engine ignores: an entry whose rules are not modelled is
/// <see cref="Unmodelled"/> and is counted, and one with genuinely no combat effect is
/// <see cref="Narrative"/> — which is a decision recorded on a curated list, never a
/// default something falls into.
/// </para>
/// <para>
/// The distinction exists because of a bug that shipped without it: an attack was
/// structured, its "if the attack roll had Advantage" qualifier was not, and the result
/// looked implemented while dealing the wrong damage on every hit. Partly-structured is
/// more dangerous than unstructured, because the missing part is invisible.
/// </para>
/// </remarks>
public enum EntryMechanics
{
    /// <summary>Resolves through an attack roll. See <see cref="MonsterEntry.Attack"/>.</summary>
    Attack,

    /// <summary>Resolves through a saving throw. See <see cref="MonsterEntry.Save"/>.</summary>
    SavingThrow,

    /// <summary>Makes several attacks. See <see cref="MonsterEntry.Multiattack"/>.</summary>
    Multiattack,

    /// <summary>A reaction, stated as a Trigger and a Response.</summary>
    Reaction,

    /// <summary>Restores hit points. See <see cref="SpellDefinition.Heal"/>.</summary>
    Healing,

    /// <summary>
    /// Examined and confirmed to have no effect on a fight — Amphibious, Illumination,
    /// and the like. Only ever set from a curated list.
    /// </summary>
    Narrative,

    /// <summary>
    /// A passive trait the engine executes by its printed name — Pack Tactics, Magic
    /// Resistance, Flyby. Only ever set from <c>MonsterTraitRegistry</c>, where the
    /// reading each name rests on is recorded.
    /// </summary>
    Passive,

    /// <summary>
    /// Real mechanics the model has no vocabulary for yet. Counted and reported rather
    /// than quietly ignored; <see cref="MonsterEntry.UnmodelledClauses"/> says what was
    /// not understood.
    /// </summary>
    Unmodelled,
}

/// <summary>The shape of an area of effect.</summary>
public enum AreaShape
{
    Cone,
    Line,
    Emanation,
    Cube,
    Sphere,
    Cylinder,
}

/// <summary>An area of effect and its dimensions.</summary>
/// <param name="Shape">The shape.</param>
/// <param name="SizeFeet">The defining dimension — a Cone's length, an Emanation's radius.</param>
/// <param name="WidthFeet">A Line's width. Null for every other shape.</param>
/// <param name="EnemiesOnly">
/// True for a printed <c>"each enemy in a ..."</c> selector — the Planetar's Holy Burst,
/// the Rakshasa's Baleful Command, the Sphinx of Lore's Mind-Rending Roar (#601) — as
/// opposed to the far more common <c>"each creature in a ..."</c>, which is false here
/// and reaches everyone the geometry covers. This is a selection rule, not a geometry
/// one: <see cref="SRDCombat.Core.Combat.AreaTargeting"/> answers "which squares", and
/// deliberately has no notion of sides to answer "which of the creatures standing in
/// them" — that filter is applied where an area's squares become a list of combatants
/// (<c>Encounter.SaveVictims</c>), against the side of whoever is using the entry.
/// </param>
public sealed record EffectArea(AreaShape Shape, int SizeFeet, int? WidthFeet = null, bool EnemiesOnly = false);

/// <summary>What a successful saving throw achieves.</summary>
public enum SaveSuccessOutcome
{
    /// <summary>The effect is avoided entirely — the SRD prints no Success line.</summary>
    NoEffect,

    /// <summary>"Success: Half damage."</summary>
    HalfDamage,

    /// <summary>
    /// The full Failure effect also applies on a success. Not produced by
    /// <c>ParseSave</c> as of #370: every printed "Failure or Success:" clause in the
    /// current corpus governs a side effect layered on top of the entry's own outcome
    /// (a Resistance carve-out, a legendary action's own-turn recharge — see
    /// <c>ParseSave</c>'s remarks), never a restatement of the Failure damage, so the
    /// label that would produce this value is never read as governing the whole entry.
    /// Kept as a representable outcome for <see cref="SaveEffect"/> and exercised by
    /// hand-authored engine tests (<c>EntrySaveTests</c>); a future printing that
    /// genuinely uses "Failure or Success:" as its sole tier would need its own
    /// anchored matcher, not a `Contains` check.
    /// </summary>
    SameAsFailure,
}

/// <summary>
/// An effect resolved by a saving throw:
/// <c>Dexterity Saving Throw: DC 12, each creature in a 30-foot Cone. Failure: 14 (4d6)
/// Acid damage. Success: Half damage.</c>
/// </summary>
/// <param name="Ability">The ability the save is made with.</param>
/// <param name="DifficultyClass">
/// The DC to beat. Null for a spell, whose DC comes from the caster's spell save DC
/// rather than from the printed text — a monster's stat block always prints one.
/// </param>
/// <param name="Area">The area, when the effect has one. Null when it targets one creature.</param>
/// <param name="FailureDamage">Damage dealt on a failed save.</param>
/// <param name="SuccessOutcome">What a successful save achieves.</param>
/// <param name="AppliedConditions">Conditions imposed on a failed save.</param>
/// <param name="CoverIgnored">
/// "The target gains no benefit from Half Cover or Three-Quarters Cover for this save."
/// Sacred Flame prints it; the sentence is structured at extraction because leaving it
/// as prose would quietly weaken the spell below its printed self the day cover landed.
/// </param>
/// <param name="ConstructsSaveAtDisadvantage">
/// "A Construct has Disadvantage on the save." Shatter prints it, exactly once in the
/// book, and Constructs are in the monster pool — an Animated Armor saving normally
/// against Shatter would be the spell executing weaker than print against exactly the
/// creatures the sentence names.
/// </param>
/// <param name="RangeFeet">
/// The distance a single-target or point-aimed save may be used at, when the entry
/// prints one — a Mummy's Dreadful Glare, "one creature the mummy can see within 30
/// feet". Null means no printed range reached this structure, which is every entry as
/// of #386's engine half: the extractor does not populate this field yet (that is
/// #386's remaining, deliberately separate half — see <c>UseSaveEntry</c>'s remarks),
/// and every spell's <see cref="SaveEffect"/> too, since a spell's own range already
/// lives on <see cref="SpellDefinition.RangeFeet"/> and is checked there before this
/// field would ever be read. Null is read as "unenforced", the same convention
/// <see cref="SpellDefinition.TargetRangeFeet"/> uses — not as "melee reach" or any
/// other stand-in distance, because a printed area effect (a Cone, a Cube centred on
/// the caster) has no target range to enforce in the first place and must keep
/// resolving exactly as it does today.
/// </param>
/// <param name="TargetSpeedDecreaseFeet">
/// The Speed reduction a failed save prints — the Steam Mephit's Steam Breath: "the
/// target's Speed decreases by 10 feet until the end of the mephit's next turn"
/// (SRD 5.2.1 p. 308). Null for the overwhelming majority of saves, which print no
/// such rider. Unlike the Slow Weapon Mastery property (p. 90, "until the <em>start</em>
/// of your next turn"), the printed boundary here is the <em>end</em> of the named
/// imposer's next turn — the imposer is explicitly named ("the mephit's"), unlike the
/// Ettin's Disadvantage rider (<see cref="MonsterAttack.ImposesDisadvantageOnTargetsNextAttack"/>),
/// so this is unambiguously the <b>source's</b> clock (<see cref="ConditionDurationOwner.Source"/>)
/// rather than the bearer's, and the boundary does not match the Slow mastery
/// property either — reusing that mechanism verbatim would release the target half a
/// round early. Only ever populated for the named-imposer phrasing; an unnamed "its
/// next turn" variant of this same rider (seen elsewhere in the corpus on plain
/// Attack entries, out of #665's scope) is not structured here. Reading by designer,
/// #665.
/// </param>
public sealed record SaveEffect(
    Ability Ability,
    int? DifficultyClass,
    EffectArea? Area,
    IReadOnlyList<AttackDamage> FailureDamage,
    SaveSuccessOutcome SuccessOutcome,
    IReadOnlyList<AppliedCondition> AppliedConditions,
    bool CoverIgnored = false,
    bool ConstructsSaveAtDisadvantage = false,
    int? RangeFeet = null,
    int? TargetSpeedDecreaseFeet = null);

/// <summary>
/// An effect that restores hit points: "regains a number of Hit Points equal to 2d8 plus
/// your spellcasting ability modifier".
/// </summary>
/// <remarks>
/// <para>
/// The third effect shape a spell can have, after an attack roll and a saving throw. Its
/// absence was not a small gap: with no healing at all, a character who dropped could
/// never be brought back up, and a run through the gauntlet died out within a few fights
/// however easy the fights were.
/// </para>
/// <para>
/// Only <b>single-target</b> healing is modelled. The mass spells — Mass Cure Wounds,
/// Mass Healing Word, Prayer of Healing — say "choose up to six creatures", which is a
/// chosen set rather than an area and needs a casting call that takes several targets;
/// Prayer of Healing also grants the benefits of a Short Rest, which is a second rule
/// again. They stay <see cref="EntryMechanics.Unmodelled"/> and counted, rather than
/// being approximated as single-target spells that quietly heal one creature of six.
/// </para>
/// </remarks>
/// <param name="Dice">The dice rolled, before the caster's modifier.</param>
/// <param name="AddsSpellcastingModifier">
/// True when the printed text adds "your spellcasting ability modifier" — Cure Wounds and
/// Healing Word both do, and Prayer of Healing's flat 2d8 does not.
/// </param>
public sealed record SpellHeal(DiceExpression Dice, bool AddsSpellcastingModifier);

/// <summary>
/// An effect that returns the dead to life: "You touch a creature that has died within
/// the last minute. That creature revives with 1 Hit Point."
/// </summary>
/// <remarks>
/// The fourth effect shape (#119), and the printed answer to the way runs actually end:
/// a death stops a character earning experience, the party diverges, and the next fight
/// is priced for four and fought by three. The trailing printed clauses — "can't revive
/// a creature that has died of old age, nor does it restore any missing body parts" —
/// are read as satisfied by construction, since neither age nor body parts exist in the
/// model. "Within the last minute" is the engine's business, not this record's: the
/// ten-round reading lives on <c>Encounter</c>, beside the same interpretation
/// <c>ConditionDuration.ForMinutes</c> states.
/// </remarks>
/// <param name="HitPoints">The hit points the creature revives with — Revivify prints 1.</param>
public sealed record SpellRevival(int HitPoints);

/// <summary>Which turn boundary a condition ends on.</summary>
public enum ConditionClock
{
    /// <summary>"until the start of ... next turn".</summary>
    StartOfTurn,

    /// <summary>"until the end of ... next turn".</summary>
    EndOfTurn,
}

/// <summary>Whose next turn a duration is counted against.</summary>
/// <remarks>
/// The SRD's wording decides this and the two readings are not interchangeable. "until
/// the end of <em>its</em> next turn" is the creature carrying the condition; "until the
/// start of <em>the devil's</em> next turn" is the creature that imposed it. Getting them
/// the wrong way round changes how long the condition lasts by most of a round.
/// </remarks>
public enum ConditionDurationOwner
{
    /// <summary>"its next turn" — the creature carrying the condition.</summary>
    Bearer,

    /// <summary>"the devil's next turn" — whoever imposed it.</summary>
    Source,
}

/// <summary>
/// How long a condition lasts: "until the start of the devil's next turn", or
/// "for 1 minute".
/// </summary>
/// <remarks>
/// <para>
/// Three shapes are modelled, all riding the same turn counter. The two turn-boundary
/// shapes are <see cref="TurnsAhead"/> = 1. A timed duration is a stated interpretation,
/// recorded here the way <c>AreaTargeting</c> records geometry: <b>"for 1 minute" ends
/// at the end of the bearer's tenth turn counting from application</b> — a minute is ten
/// rounds, and the bearer's own turn is the boundary the SRD's repeated-save wordings
/// measure against. <b>"for 1 hour" and anything longer outlasts any fight</b>
/// (<see cref="OutlastsFight"/>), so the condition ends only with the encounter; the
/// printed duration is still recorded rather than rounded to a number no fight reaches.
/// </para>
/// <para>
/// The repeated save became a modelled way out with Hold Person:
/// <see cref="RepeatSaveAtTurnEnd"/> rolls the same save at the end of each of the
/// bearer's turns, and <see cref="WhileConcentrating"/> ties the condition's life to
/// its caster's Concentration. Still unmodelled and staying in
/// <see cref="AppliedCondition.UnmodelledRequirement"/>: "until the grapple ends"
/// outside its sibling grapple, and "until the web is destroyed" (which needs an
/// object with hit points). "Until it takes damage" now has a hook, but a narrow
/// one rather than a general duration shape: Turn Undead's rider is the one printed
/// effect that ends early on damage, on its source's Incapacitated condition, or on
/// its source's death, and none of those is a clock, a repeat save or a
/// Concentration tie — so <see cref="Combat.ActiveCondition.EndsEarlyOnDamageOrSourceDown"/>
/// is a closed-set flag beside this record rather than a fourth shape here. A rider
/// printing "until it takes damage" with no further tie to Turn Undead's own wording
/// still has nothing on <see cref="ConditionDuration"/> to reach for.
/// </para>
/// </remarks>
/// <param name="Clock">Which boundary of the owner's turn it ends on.</param>
/// <param name="Owner">Whose turn is counted.</param>
/// <param name="TurnsAhead">
/// How many of the owner's turns ahead the boundary lies. 1 is "next turn"; 10 is
/// "for 1 minute". Not consulted when <paramref name="OutlastsFight"/> is set.
/// </param>
/// <param name="OutlastsFight">
/// True for a printed duration no fight reaches — "for 1 hour", "for 24 hours". The
/// condition gets no expiry and ends with the encounter.
/// </param>
/// <param name="WhileGrappleHolds">
/// True for "until the grapple ends". The condition gets no expiry of its own: it is
/// imposed only while the same creature's grapple holds the target, and
/// <c>Encounter.EndGrapple</c> takes it away with the grapple, however the grapple
/// ended — escape, incapacity or distance.
/// </param>
public sealed record ConditionDuration(
    ConditionClock Clock,
    ConditionDurationOwner Owner,
    int TurnsAhead = 1,
    bool OutlastsFight = false,
    bool WhileGrappleHolds = false,
    bool WhileConcentrating = false,
    bool RepeatSaveAtTurnEnd = false)
{
    /// <summary>"for N minutes": ten of the bearer's turns per minute, ending at the end of a turn.</summary>
    public static ConditionDuration ForMinutes(int minutes) =>
        new(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer, minutes * 10);

    /// <summary>
    /// Hold Person's whole printed clock: "for the duration" on a Concentration spell
    /// capped at 1 minute, with "the target repeats the save at the end of each of its
    /// turns, ending the spell on itself on a success". Three ways out, whichever
    /// comes first — the caster's Concentration breaks, the bearer's save succeeds,
    /// or the bearer's tenth turn ends.
    /// </summary>
    public static ConditionDuration ConcentrationUpToOneMinuteWithRepeatSave { get; } =
        new(
            ConditionClock.EndOfTurn,
            ConditionDurationOwner.Bearer,
            TurnsAhead: 10,
            WhileConcentrating: true,
            RepeatSaveAtTurnEnd: true);

    /// <summary>"for 1 hour" and longer: printed time no fight reaches.</summary>
    public static ConditionDuration BeyondTheFight { get; } =
        new(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer, 0, OutlastsFight: true);

    /// <summary>"until the grapple ends": lives and dies with the sibling grapple.</summary>
    public static ConditionDuration UntilTheGrappleEnds { get; } =
        new(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer, 0, WhileGrappleHolds: true);

    /// <summary>
    /// The stat blocks' own repeat-save clock: "repeats the save at the end of each of
    /// its turns, ending the effect on itself on a success. After 1 minute, it succeeds
    /// automatically." The automatic success is the ten-turn cap — the same reading as
    /// <see cref="ForMinutes"/> — and unlike Hold Person's there is no Concentration to
    /// break, so the ways out are the save and the clock.
    /// </summary>
    public static ConditionDuration RepeatSaveUpToOneMinute { get; } =
        new(
            ConditionClock.EndOfTurn,
            ConditionDurationOwner.Bearer,
            TurnsAhead: 10,
            RepeatSaveAtTurnEnd: true);

    /// <summary>
    /// The two-tier gaze's clock: "repeats the save at the end of its next turn if it
    /// is still Restrained, ending the effect on itself on a success", with a deeper
    /// tier waiting on the failure. No calendar at all — the ways out are the repeated
    /// save and the escalation, and the escalation resolves the repeat either way, so
    /// "each of its turns" and "its next turn" are the same clock here: there is never
    /// a second repeat to roll.
    /// </summary>
    public static ConditionDuration UntilSavedOrEscalated { get; } =
        new(
            ConditionClock.EndOfTurn,
            ConditionDurationOwner.Bearer,
            TurnsAhead: 0,
            OutlastsFight: true,
            RepeatSaveAtTurnEnd: true);
}

/// <summary>
/// A condition an entry imposes — "If the target is a Large or smaller creature, it has
/// the Grappled condition (escape DC 13)".
/// </summary>
/// <remarks>
/// <para>
/// The condition is rarely the whole rule. It is nearly always printed with something
/// attached: a gate on the target's size, a duration, a pull, a second condition that
/// lasts until the first one ends. Capturing the condition and dropping the rest is the
/// goblin conditional-damage bug in a new place — the rider would fire in more cases, or
/// for longer, than the SRD allows, and nothing would say so.
/// </para>
/// <para>
/// So exactly one qualifier is modelled — <see cref="MaximumTargetSize"/> — and anything
/// else printed alongside the condition lands in <see cref="UnmodelledRequirement"/>,
/// which makes the rider unusable rather than approximate. See
/// <c>SRDCombat.Core.Rules.ConditionRules</c> for the other half of the decision: whether
/// the engine executes the condition at all.
/// </para>
/// </remarks>
/// <param name="Condition">The condition.</param>
/// <param name="EscapeDifficultyClass">
/// The DC to escape, for conditions that can be escaped. Null when the printed text
/// gives none.
/// </param>
/// <param name="MaximumTargetSize">
/// The largest target the condition can be imposed on, from "If the target is a Large or
/// smaller creature". Null when the printed text gates on no size.
/// </param>
/// <param name="Duration">
/// How long it lasts, from "until the start of the devil's next turn". Null when the
/// printed text gives no duration at all, which is its own answer — Prone lasts until
/// you stand up, Grappled until you escape.
/// </param>
/// <param name="UnmodelledRequirement">
/// What was printed alongside the condition that the model cannot express — a further
/// requirement ("and the gorgon moved 20+ feet straight toward it"), a duration shape
/// outside the two modelled here ("until the grapple ends", "for 1 minute"), or a
/// trailing clause carrying its own rule. Null when the rider is nothing but the
/// condition, a size gate and a modelled duration.
/// </param>
/// <param name="EscalatesTo">
/// The condition a failed repeated save deepens this one into — the two-tier gaze's
/// "Second Failure: The target has the Petrified condition instead of the Restrained
/// condition." Null for every rider whose repeat can only end the effect. Only set by
/// the extraction template that matches that exact printed pair, and only meaningful
/// alongside a <see cref="ConditionDuration.RepeatSaveAtTurnEnd"/> duration: the
/// escalation <em>is</em> the failure outcome of the repeated save.
/// </param>
public sealed record AppliedCondition(
    ConditionType Condition,
    int? EscapeDifficultyClass = null,
    CreatureSize? MaximumTargetSize = null,
    ConditionDuration? Duration = null,
    string? UnmodelledRequirement = null,
    ConditionType? EscalatesTo = null)
{
    /// <summary>
    /// True when everything printed with this condition is expressed by the model, so
    /// imposing it does exactly what the stat block says and no more.
    /// </summary>
    public bool IsFullyModelled => UnmodelledRequirement is null;

    /// <summary>Whether a target of this size passes the printed size gate.</summary>
    public bool AllowsTargetSize(CreatureSize size) =>
        MaximumTargetSize is not { } maximum || size <= maximum;
}

/// <summary>
/// One named attack's exact share of a printed enumerated Multiattack composition —
/// <c>Bite×1</c> in "The bear makes one Bite attack and one Claw attack."
/// </summary>
/// <param name="Name">The attack's name, matched against the creature's named attacks.</param>
/// <param name="Count">How many of this attack this Attack action makes.</param>
public sealed record MultiattackComponent(string Name, int Count);

/// <summary>
/// A Multiattack: <c>The bandit makes two attacks, using Scimitar and Pistol in any
/// combination.</c>
/// </summary>
/// <param name="AttackCount">How many attacks are made.</param>
/// <param name="AttackNames">
/// The named attacks to choose from. A single name means every attack uses it; several
/// mean the creature picks.
/// </param>
/// <param name="AnyCombination">
/// True when the creature may mix the named attacks freely, false when the text names
/// one attack to repeat.
/// </param>
/// <param name="Composition">
/// Non-null exactly for a printed <em>multi-name</em> enumerated composition —
/// "one Bite attack and one Claw attack" — where the enumeration is exact and mandatory:
/// each component names an attack and how many of that attack this Attack action makes,
/// an exact per-name cap. The SRD prints substitution ("It can replace one attack with
/// …") and free combination ("in any combination") with distinct grammar; where neither
/// appears, the enumeration binds and no name may be swapped for another. Null for
/// single-name repeats and free combinations, whose <see cref="AttackCount"/>,
/// <see cref="AttackNames"/> and <see cref="AnyCombination"/> already express them in
/// full — see <see cref="SRDCombat.Core.Combat.AreaTargeting"/> for the general shape of
/// this kind of reading.
/// When set: <see cref="AnyCombination"/> is false, <see cref="AttackNames"/> equals the
/// component names in printed order, and <see cref="AttackCount"/> equals the sum of the
/// components' counts.
/// </param>
public sealed record MultiattackEffect(
    int AttackCount,
    IReadOnlyList<string> AttackNames,
    bool AnyCombination,
    IReadOnlyList<MultiattackComponent>? Composition = null);

/// <summary>How often an entry can be used.</summary>
public enum UsageLimitKind
{
    /// <summary>"(Recharge 5-6)" — rolls a d6 at the start of each turn to come back.</summary>
    Recharge,

    /// <summary>"(3/Day)".</summary>
    PerDay,

    /// <summary>"(Recharge after a Short or Long Rest)".</summary>
    RechargeAfterRest,
}

/// <summary>A limit on how often an entry can be used.</summary>
/// <param name="Kind">Which kind of limit.</param>
/// <param name="RechargeMinimum">
/// The lowest d6 result that recharges the ability — 5 for "(Recharge 5-6)". Only set
/// for <see cref="UsageLimitKind.Recharge"/>.
/// </param>
/// <param name="UsesPerDay">Uses per day. Only set for <see cref="UsageLimitKind.PerDay"/>.</param>
public sealed record UsageLimit(UsageLimitKind Kind, int? RechargeMinimum = null, int? UsesPerDay = null);

/// <summary>
/// The event that fires a reaction — the structured half of a Reaction-section entry's
/// printed Trigger clause.
/// </summary>
/// <remarks>
/// One member today: the trigger Parry keys on (#677). This enum is not populated ahead
/// of the code that reads it — #413 names the other in-pool reactions (Goblin Boss's
/// Redirect Attack, Ochre Jelly / Black Pudding Split, Octopus Ink Cloud, Rust Monster's
/// Reflexive Antennae, Sphinx of Wonder's Burst of Ingenuity), and each fires on a
/// different event and does a different thing. A member is added only when the slice that
/// executes that reaction lands, never speculatively.
/// </remarks>
public enum ReactionTrigger
{
    /// <summary>
    /// "Trigger: the creature is hit by a melee attack roll while holding a weapon."
    /// Parry (#677/#678) — every corpus creature printing that exact reaction shape:
    /// Bandit Captain, Knight, Warrior Veteran, Noble (AC bonus 2), Gladiator (3),
    /// Erinyes (4), Marilith (5).
    /// </summary>
    HitByMeleeAttack,
}

/// <summary>
/// The structured, engine-executable form of a reaction: its trigger and, for the only
/// response shape modelled today, the Armor Class bonus it adds against the attack that
/// triggered it.
/// </summary>
/// <remarks>
/// This carries Parry's whole mechanic (#677): a deterministic recompute of an
/// already-rolled melee attack against the raised AC, spending no dice — see
/// <c>Encounter.TryParry</c> for where it resolves and the reading it rests on. A
/// reaction whose response is <em>not</em> "raise AC by N" (a retarget, a split, a
/// blinding gaze) is its own shape and does not reuse this record: it gets its own
/// structured signal alongside the code that executes it, per the no-speculative-
/// abstraction rule. #678 taught the extractor to fill this from content — see
/// <c>EntryMechanicsParser.ParseParryReaction</c> for the recognised shape and the
/// misattribution risk (Riposte, Whirlwind of Sand) it is gated against — and
/// hand-authored fixtures remain the engine-level tests' own source, unaffected by
/// content regeneration.
/// </remarks>
/// <param name="Trigger">The event that fires the reaction.</param>
/// <param name="ArmorClassBonus">
/// The bonus the response adds to the reacting creature's Armor Class against the
/// triggering attack — the "adds 2 to its AC" of Parry.
/// </param>
public sealed record ExecutableReaction(ReactionTrigger Trigger, int ArmorClassBonus);

/// <summary>A reaction's trigger and what it does in response.</summary>
/// <param name="Trigger">The printed Trigger clause, verbatim, for narration.</param>
/// <param name="Response">The printed Response clause, verbatim, for narration.</param>
public sealed record ReactionEffect(string Trigger, string Response)
{
    /// <summary>
    /// The structured, engine-executable form of this reaction, when the model can
    /// resolve it — null for every reaction still carried only as the verbatim
    /// <see cref="Trigger"/>/<see cref="Response"/> prose (design §2.2: storing prose is
    /// not executing it, so an unmodelled reaction leaves its clause in residue). Set
    /// for Parry (#677); see <see cref="ExecutableReaction"/> for what it carries and why
    /// nothing more general lives here yet.
    /// </summary>
    public ExecutableReaction? Executable { get; init; }
}

/// <summary>Which turn boundary an aura's recurring save fires on.</summary>
/// <remarks>
/// One member today. An aura is a passive area effect a creature emits on its own clock,
/// and the clock is part of the signal because the SRD prints more than one: the Ghast's
/// Stench fires at the <em>victim's</em> turn start ("any creature that <b>starts its
/// turn</b> in a 5-foot Emanation", SRD 5.2.1 p. 287), while the Azer's Fire Aura fires at
/// the <em>emitter's</em> turn end ("At the end of each of its turns"). Only the first is
/// modelled here (#670); the second is out of scope and would add its own member alongside
/// the code that fires it, never speculatively — the same discipline
/// <see cref="ReactionTrigger"/> keeps.
/// </remarks>
public enum AuraClock
{
    /// <summary>
    /// "any creature that starts its turn in [range]" — the save is forced at the start
    /// of each potential victim's turn, against every emitter of such an aura within
    /// range. The Ghast's Stench.
    /// </summary>
    StartOfVictimTurn,
}

/// <summary>
/// The structured signal that a stat block entry is an <b>aura</b>: a passive per-turn
/// area effect a creature emits, resolved through the entry's own <see cref="SaveEffect"/>
/// but on the aura's clock rather than as a spent action. The Ghast's Stench (#670,
/// SRD 5.2.1 p. 287) is the one in-pool case this executes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reading, written down (following the <see cref="SRDCombat.Core.Combat.AreaTargeting"/>
/// model).</b> Print: "Stench. Constitution Saving Throw: DC 10, any creature that starts
/// its turn in a 5-foot Emanation originating from the ghast. Failure: The target has the
/// Poisoned condition until the start of its next turn. Success: The target is immune to
/// this ghast's Stench for 24 hours."
/// </para>
/// <list type="bullet">
/// <item><b>When it fires.</b> At the start of a potential victim's turn (see
/// <see cref="Clock"/>), the engine forces the save against that one victim for each
/// emitter within range — <em>not</em> as an area sweep catching everyone at once. Stench
/// only touches a creature on the turn <em>that creature</em> starts in range, which is
/// what makes it a per-turn recurrence rather than a one-shot blast. See
/// <c>Encounter.FireAuras</c>.</item>
/// <item><b>Who it fires for.</b> "any creature ... friend or foe" — the emitter's own
/// side is not spared. The emitter itself is excluded: an Emanation's origin "isn't
/// included in the area of effect unless its creator decides otherwise" (SRD 5.2.1 p. 181),
/// and Stench decides nothing otherwise. It fires for every <em>living</em> victim that
/// starts its turn in range, an Incapacitated or dying one included: the printed trigger is
/// "any creature that starts its turn", and a creature has started its turn the moment its
/// turn clock ticks, whether or not it can act. The save is worth rolling even for a downed
/// victim because a success banks the immunity below — a consequence independent of whether
/// the Poisoned rider lands. The rider itself still does not land on such a victim:
/// <c>Encounter.ImposeConditions</c> takes nothing further on an Incapacitated or dying
/// creature, so the save can grant it immunity without ever Poisoning it while it is
/// down.</item>
/// <item><b>Emitter state.</b> The emanation exists as long as its origin does. A living
/// emitter emits even while Incapacitated: Stench prints no off-switch, and the contrast is
/// deliberate in the book — Aura of Protection explicitly reads "inactive while you have
/// the Incapacitated condition" (SRD 5.2.1 p. 55), a clause Stench does not carry. A
/// <b>dead</b> emitter emits nothing: no origin, no emanation.</item>
/// <item><b>The 24-hour immunity.</b> "immune to this ghast's Stench for 24 hours" — no
/// fight reaches 24 hours, so a creature that <b>succeeds</b> is immune for the rest of the
/// encounter and never rolls against that emitter's aura again. A creature that
/// <b>fails</b> gains no immunity and re-rolls at the start of each of its turns, the
/// condition re-applying until it eventually saves. The immunity is keyed per
/// (victim, emitter, aura), so saving against one ghast does not grant immunity to another.
/// This set is what keeps the dice stream honest across turns: without it a saver would
/// re-roll every turn and consume dice it should not.</item>
/// </list>
/// <para>
/// <b>Deliberately minimal.</b> This carries only what Stench needs. The radius lives here
/// as a plain number so the per-victim range check reads one field and does not route
/// through the area-geometry machinery (which answers "who is in the area now", the wrong
/// question for an aura). Stench deals no damage; a future <em>damaging</em> aura firing at
/// turn start could down a victim before its turn resolves, which <c>Encounter.FireAuras</c>
/// would then have to account for — out of scope here, and noted rather than built ahead of
/// a case that exists. The extractor now populates this field for the Ghast's Stench
/// (#676, the paired reclassification that followed this seam, the same
/// engine-then-extractor split #386/#421 used) — every other entry still leaves it null,
/// and engine tests continue to author a hand-built aura for shapes the extractor does not
/// classify.
/// </para>
/// </remarks>
/// <param name="EmanationRadiusFeet">
/// The radius of the emanation, in feet — 5 for the Ghast. A victim within this distance of
/// the emitter (nearest-space, SRD 5.2.1 p. 13) is in range. Equals the emitter entry's
/// <see cref="SaveEffect.Area"/> size where one is present, but read here directly so the
/// range gate does not depend on the save carrying an area shape.
/// </param>
/// <param name="Clock">Which turn boundary fires the aura's save.</param>
public sealed record AuraEffect(int EmanationRadiusFeet, AuraClock Clock);

/// <summary>
/// The structured signal that a stat block entry is a <b>death burst</b>: an on-death area
/// save, resolved through the entry's own <see cref="MonsterEntry.Save"/> exactly as any
/// other stat-block saving-throw entry would be, but fired once — automatically, when its
/// carrier dies — rather than spent as an action. The four mephits' and the Magmin's
/// "Death Burst" trait (#679, SRD 5.2.1 pp. 300, 320-321).
/// </summary>
/// <remarks>
/// <para>
/// <b>The reading, written down (following the <see cref="SRDCombat.Core.Combat.AreaTargeting"/>
/// model).</b> Print (Magma Mephit): "Death Burst. The mephit explodes when it dies.
/// Dexterity Saving Throw: DC 11, each creature in a 5-foot Emanation originating from the
/// mephit. Failure: 7 (2d6) Fire damage. Success: Half damage." Every carrier's save is
/// already fully structured — DC, ability, area, failure damage and success outcome are all
/// present on <see cref="MonsterEntry.Save"/> — so this marker claims exactly one clause,
/// "explodes when it dies": the trigger, not the effect. No radius or clock lives on this
/// type the way <see cref="AuraEffect"/> carries its own, because a death burst is a
/// one-shot area catch rather than a per-victim recurring check — <c>Encounter.FireDeathBurst</c>
/// wants the ordinary area-geometry machinery (<c>SaveVictims</c>/<c>AreaTargeting</c>) that
/// answers "who is in the area right now", which is exactly the question a one-shot burst
/// asks and exactly the question <see cref="AuraEffect"/>'s own remarks say an aura must
/// <em>not</em> ask. <see cref="MonsterEntry.Save"/>'s own <c>Area</c> already carries the
/// Emanation and its size, so re-stating the radius here would be a second copy to drift.
/// </para>
/// <list type="bullet">
/// <item><b>When it fires.</b> The instant the carrier's own death is recorded — see
/// <c>Encounter.MarkDied</c>, the one place across the engine's several death-producing
/// paths (a lethal hit, massive damage, a third failed death save, another creature's own
/// area save) that narrates <c>CombatStepKind.Died</c> and is now also where
/// <c>FireDeathBurst</c> is called. Firing there rather than at removal means the burst
/// resolves before <c>Encounter.EndTurnEffectsWhoseSourceIsDown</c>/<c>EndBrokenGrapples</c>
/// tidy up anything the carrier's own death would otherwise leave dangling, and before the
/// carrier is dropped from initiative — though nothing here currently depends on either
/// order, since the burst neither reads nor is read by the carrier's own ended effects.
/// </item>
/// <item><b>Who it excludes.</b> By the time <c>MarkDied</c> calls <c>FireDeathBurst</c>,
/// the carrier's own <c>IsDead</c> is already true — every death-producing path sets it
/// before narrating the death — so the ordinary area-geometry catch
/// (<c>Encounter.CreaturesIn</c>, gated on <c>Combatant.IsActive</c>) excludes the carrier
/// from its own blast automatically, with no special-casing needed: a dead creature is
/// never <c>IsActive</c>. This happens to match the printed default that an Emanation's
/// origin isn't included in its own area (SRD 5.2.1 p. 181) — the mephit is not there to
/// be caught by its own explosion, being what exploded.
/// </item>
/// <item><b>The cascade — does a burst-killed carrier's own burst fire?</b> <b>Yes.</b>
/// The print reads "the mephit explodes when it dies", with no exception carved out for
/// what killed it; a mephit killed by a second mephit's Death Burst has died exactly as
/// much as one killed by a sword, and nothing in the entry's wording is conditioned on the
/// cause. <c>Encounter.MarkDied</c> is the recursive hook that makes this work for free: a
/// victim of one burst that itself dies is marked dead — and so bursts — through the exact
/// same method, nested inside the outer burst's own victim loop, before that loop resolves
/// its next victim. This is finite by construction and needs no depth limit: a creature's
/// death is recorded exactly once (<c>MarkDied</c> is reached once per creature per fight —
/// nothing revives and re-kills inside a single resolution), so the recursion strictly
/// consumes distinct, still-living combatants and cannot revisit one. <c>FireDeathBurst</c>
/// additionally guards itself with a fire-once set keyed on the carrier's id, defence in
/// depth against a future change to the death paths reintroducing a double call for the
/// same creature.
/// </item>
/// </list>
/// <para>
/// Null for every entry the extractor has not classified this way — set by #679's own
/// regeneration for the five "Death Burst" traits (Magmin, Dust/Ice/Magma/Steam Mephit)
/// and, since the pattern reads printed grammar rather than a creature's name, the
/// identically-shaped Balor's "Death Throes" for free (six entries, one clause each).
/// </para>
/// </remarks>
public sealed record DeathBurstEffect;

/// <summary>Helpers over a damage list, shared by attacks and saving-throw effects.</summary>
public static class DamageComponents
{
    /// <summary>The average total of a damage list, ignoring conditional components.</summary>
    public static int AverageOfUnconditional(IReadOnlyList<AttackDamage> damage)
    {
        ArgumentNullException.ThrowIfNull(damage);

        return damage.Where(component => component.Condition is null).Sum(component => component.Amount.Average);
    }

    /// <summary>Halves every component, as a successful save against damage does.</summary>
    public static int Halve(int amount) => amount / 2;

    /// <summary>The printed averages of a list, for narration and validation.</summary>
    public static IEnumerable<DiceExpression> Expressions(IReadOnlyList<AttackDamage> damage)
    {
        ArgumentNullException.ThrowIfNull(damage);

        return damage.Select(component => component.Amount);
    }
}
