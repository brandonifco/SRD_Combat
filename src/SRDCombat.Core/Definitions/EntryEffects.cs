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
public sealed record EffectArea(AreaShape Shape, int SizeFeet, int? WidthFeet = null);

/// <summary>What a successful saving throw achieves.</summary>
public enum SaveSuccessOutcome
{
    /// <summary>The effect is avoided entirely — the SRD prints no Success line.</summary>
    NoEffect,

    /// <summary>"Success: Half damage."</summary>
    HalfDamage,

    /// <summary>"Failure or Success:" — something happens either way.</summary>
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
public sealed record SaveEffect(
    Ability Ability,
    int? DifficultyClass,
    EffectArea? Area,
    IReadOnlyList<AttackDamage> FailureDamage,
    SaveSuccessOutcome SuccessOutcome,
    IReadOnlyList<AppliedCondition> AppliedConditions,
    bool CoverIgnored = false);

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
/// Still unmodelled and staying in <see cref="AppliedCondition.UnmodelledRequirement"/>:
/// "until the grapple ends" (which needs the grapple), "until the web is destroyed"
/// (which needs an object with hit points), and any duration printed with an early out —
/// "until it takes damage", a repeated save — because imposing the timer without the way
/// out would hold the condition longer than the book says.
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
    bool WhileGrappleHolds = false)
{
    /// <summary>"for N minutes": ten of the bearer's turns per minute, ending at the end of a turn.</summary>
    public static ConditionDuration ForMinutes(int minutes) =>
        new(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer, minutes * 10);

    /// <summary>"for 1 hour" and longer: printed time no fight reaches.</summary>
    public static ConditionDuration BeyondTheFight { get; } =
        new(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer, 0, OutlastsFight: true);

    /// <summary>"until the grapple ends": lives and dies with the sibling grapple.</summary>
    public static ConditionDuration UntilTheGrappleEnds { get; } =
        new(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer, 0, WhileGrappleHolds: true);
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
public sealed record AppliedCondition(
    ConditionType Condition,
    int? EscapeDifficultyClass = null,
    CreatureSize? MaximumTargetSize = null,
    ConditionDuration? Duration = null,
    string? UnmodelledRequirement = null)
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
public sealed record MultiattackEffect(
    int AttackCount,
    IReadOnlyList<string> AttackNames,
    bool AnyCombination);

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

/// <summary>A reaction's trigger and what it does in response.</summary>
/// <param name="Trigger">The printed Trigger clause.</param>
/// <param name="Response">The printed Response clause.</param>
public sealed record ReactionEffect(string Trigger, string Response);

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
