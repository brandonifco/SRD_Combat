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

    /// <summary>
    /// Examined and confirmed to have no effect on a fight — Amphibious, Illumination,
    /// and the like. Only ever set from a curated list.
    /// </summary>
    Narrative,

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
/// <param name="DifficultyClass">The DC to beat.</param>
/// <param name="Area">The area, when the effect has one. Null when it targets one creature.</param>
/// <param name="FailureDamage">Damage dealt on a failed save.</param>
/// <param name="SuccessOutcome">What a successful save achieves.</param>
/// <param name="AppliedConditions">Conditions imposed on a failed save.</param>
public sealed record SaveEffect(
    Ability Ability,
    int DifficultyClass,
    EffectArea? Area,
    IReadOnlyList<AttackDamage> FailureDamage,
    SaveSuccessOutcome SuccessOutcome,
    IReadOnlyList<AppliedCondition> AppliedConditions);

/// <summary>
/// A condition an entry imposes — "the target has the Grappled condition (escape DC 13)".
/// </summary>
/// <param name="Condition">The condition.</param>
/// <param name="EscapeDifficultyClass">
/// The DC to escape, for conditions that can be escaped. Null when the printed text
/// gives none.
/// </param>
public sealed record AppliedCondition(ConditionType Condition, int? EscapeDifficultyClass = null);

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
