using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Rules;

/// <summary>Why an attack roll was made with Advantage or Disadvantage.</summary>
/// <param name="TargetIsDodging">The target took the Dodge action.</param>
/// <param name="TargetIsProne">The target is Prone.</param>
/// <param name="TargetIsUnconscious">The target is Unconscious.</param>
/// <param name="AttackerIsProne">The attacker is Prone.</param>
/// <param name="AttackerIsPoisoned">The attacker is Poisoned.</param>
/// <param name="AttackerIsRestrained">The attacker is Restrained.</param>
/// <param name="TargetIsRestrained">The target is Restrained.</param>
/// <param name="AttackerIsGrappledByAnother">
/// The attacker is Grappled and this is not its grappler. Grappled imposes Disadvantage
/// only on attacks against someone <em>other</em> than the creature holding you, so this
/// is the one circumstance that depends on who is being attacked rather than on the
/// attacker alone.
/// </param>
/// <param name="AtLongRange">The target is beyond the attack's normal range.</param>
/// <param name="AttackerIsBlinded">The attacker is Blinded.</param>
/// <param name="TargetIsBlinded">The target is Blinded.</param>
/// <param name="AttackerIsFrightened">
/// The attacker is Frightened. The printed Disadvantage applies "while the source of
/// fear is within line of sight", and the engine has no model of sight — the source is
/// read as always visible while it is on the field. The reading is recorded on
/// <c>ConditionRules</c>.
/// </param>
/// <param name="TargetIsParalyzed">The target is Paralyzed.</param>
/// <param name="TargetIsStunned">The target is Stunned.</param>
/// <remarks>
/// Every one defaults to false, because false is "nothing unusual is true" for all of
/// them. That keeps a caller naming only the circumstance it cares about, and means the
/// next condition to be implemented adds a parameter without breaking anyone.
/// </remarks>
public sealed record AttackCircumstances(
    bool TargetIsDodging = false,
    bool TargetIsProne = false,
    bool TargetIsUnconscious = false,
    bool AttackerIsProne = false,
    bool AttackerIsPoisoned = false,
    bool AttackerIsRestrained = false,
    bool TargetIsRestrained = false,
    bool AttackerIsGrappledByAnother = false,
    bool AtLongRange = false,
    bool AttackerIsBlinded = false,
    bool TargetIsBlinded = false,
    bool AttackerIsFrightened = false,
    bool TargetIsParalyzed = false,
    bool TargetIsStunned = false);

/// <summary>The outcome of one attack roll, before damage is applied.</summary>
/// <param name="Roll">The d20 roll.</param>
/// <param name="Hit">Whether it hit.</param>
/// <param name="Critical">Whether it is a Critical Hit.</param>
/// <param name="TargetArmorClass">The AC it was rolled against.</param>
/// <param name="Circumstances">What produced the roll's mode.</param>
public sealed record AttackRoll(
    D20Roll Roll,
    bool Hit,
    bool Critical,
    int TargetArmorClass,
    AttackCircumstances Circumstances);

/// <summary>Resolving attack rolls.</summary>
public static class AttackRules
{
    /// <summary>Works out the Advantage and Disadvantage applying to an attack.</summary>
    public static AttackCircumstances DescribeCircumstances(
        Combatant attacker,
        CombatAttack attack,
        Combatant target)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(target);

        var distance = attacker.Position.DistanceFeetTo(target.Position);

        return new AttackCircumstances(
            // Dodge is lost while Incapacitated, so an unconscious dodger gets nothing.
            TargetIsDodging: target.Turn.IsDodging && !target.HasCondition(ConditionType.Incapacitated),
            TargetIsProne: target.HasCondition(ConditionType.Prone),
            TargetIsUnconscious: target.HasCondition(ConditionType.Unconscious),
            AttackerIsProne: attacker.HasCondition(ConditionType.Prone),
            AttackerIsPoisoned: attacker.HasCondition(ConditionType.Poisoned),
            AttackerIsRestrained: attacker.HasCondition(ConditionType.Restrained),
            TargetIsRestrained: target.HasCondition(ConditionType.Restrained),
            AttackerIsGrappledByAnother: IsGrappledBySomeoneElse(attacker, target),
            AtLongRange: attack.IsAtLongRange(distance),
            AttackerIsBlinded: attacker.HasCondition(ConditionType.Blinded),
            TargetIsBlinded: target.HasCondition(ConditionType.Blinded),
            AttackerIsFrightened: attacker.HasCondition(ConditionType.Frightened),
            TargetIsParalyzed: target.HasCondition(ConditionType.Paralyzed),
            TargetIsStunned: target.HasCondition(ConditionType.Stunned));
    }

    /// <summary>
    /// Whether the attacker is Grappled by a creature other than the one it is attacking.
    /// </summary>
    /// <remarks>
    /// "You have Disadvantage on attack rolls against any target other than the grappler."
    /// Hitting back at whatever has hold of you is the one attack a grapple does not
    /// hamper, and reading the condition as a blanket penalty would quietly take that
    /// away.
    /// </remarks>
    private static bool IsGrappledBySomeoneElse(Combatant attacker, Combatant target)
    {
        if (attacker.ConditionState(ConditionType.Grappled) is not { } grapple)
        {
            return false;
        }

        // A grapple with no recorded grappler cannot be aimed out of, so it hampers
        // everything — the safe direction when the source is unknown.
        return !string.Equals(grapple.SourceId, target.Id, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reduces the circumstances to a single roll mode.
    /// </summary>
    /// <remarks>
    /// Advantage and Disadvantage never stack and always cancel, so this collects every
    /// source of each and combines once. A worked example the rules produce and which is
    /// easy to get wrong: attacking an Unconscious creature from more than 5 feet away
    /// gives Advantage (Unconscious) and Disadvantage (Prone, attacker not within 5
    /// feet), so the roll is made normally.
    /// </remarks>
    public static RollMode ResolveRollMode(AttackCircumstances circumstances, int distanceFeet)
    {
        ArgumentNullException.ThrowIfNull(circumstances);

        var withinFiveFeet = distanceFeet <= Battlefield.FeetPerSquare;

        var advantage =
            circumstances.TargetIsUnconscious
            || circumstances.TargetIsRestrained
            || circumstances.TargetIsBlinded
            || circumstances.TargetIsParalyzed
            || circumstances.TargetIsStunned
            || (circumstances.TargetIsProne && withinFiveFeet);

        var disadvantage =
            circumstances.TargetIsDodging
            || circumstances.AttackerIsProne
            || circumstances.AttackerIsPoisoned
            || circumstances.AttackerIsRestrained
            || circumstances.AttackerIsGrappledByAnother
            || circumstances.AtLongRange
            || circumstances.AttackerIsBlinded
            || circumstances.AttackerIsFrightened
            || (circumstances.TargetIsProne && !withinFiveFeet);

        return D20Test.Combine(advantage, disadvantage);
    }

    /// <summary>Rolls one attack.</summary>
    /// <param name="extraAdvantage">
    /// Advantage from a source this method does not know about, such as a Barbarian's
    /// Reckless Attack. Combined with the rest, so it still cancels against Disadvantage
    /// rather than overriding it.
    /// </param>
    /// <param name="extraDisadvantage">Disadvantage from such a source.</param>
    public static AttackRoll Resolve(
        IRandomSource random,
        Combatant attacker,
        CombatAttack attack,
        Combatant target,
        bool extraAdvantage = false,
        bool extraDisadvantage = false)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(target);

        var distance = attacker.Position.DistanceFeetTo(target.Position);
        var circumstances = DescribeCircumstances(attacker, attack, target);
        var mode = D20Test.Combine(ResolveRollMode(circumstances, distance), extraAdvantage, extraDisadvantage);

        var roll = D20Test.Roll(random, attack.AttackBonus, mode);

        // A natural 20 always hits and is always a Critical Hit; a natural 1 always
        // misses. Both ignore modifiers and AC entirely.
        if (roll.IsNatural1)
        {
            return new AttackRoll(roll, Hit: false, Critical: false, target.Stats.ArmorClass, circumstances);
        }

        var hit = roll.IsNatural20 || roll.Total >= target.Stats.ArmorClass;

        // Unconscious and Paralyzed print the same clause: any hit from within 5 feet
        // is a Critical Hit. Stunned deliberately does not — the glossary gives it
        // Advantage against and nothing more.
        var critical = roll.IsNatural20
            || (hit
                && (circumstances.TargetIsUnconscious || circumstances.TargetIsParalyzed)
                && distance <= Battlefield.FeetPerSquare);

        return new AttackRoll(roll, hit, critical, target.Stats.ArmorClass, circumstances);
    }

    /// <summary>
    /// Rolls an attack's damage, doubling the dice on a Critical Hit and skipping any
    /// component whose condition is not met.
    /// </summary>
    /// <remarks>
    /// The conditional case is rare but real, and getting it wrong is invisible: a
    /// Goblin Warrior deals "plus 2 (1d4) Slashing damage if the attack roll had
    /// Advantage", and applying that unconditionally makes it hit for half again as much
    /// as the SRD says on every ordinary swing.
    /// </remarks>
    public static IReadOnlyList<(AttackDamage Component, DiceRollResult Result)> RollDamage(
        IRandomSource random,
        CombatAttack attack,
        AttackRoll roll)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(roll);

        return attack.Damage
            .Where(component => Applies(component.Condition, roll))
            .Select(component => (component, DiceRoller.Roll(random, component.Amount, roll.Critical)))
            .ToArray();
    }

    /// <summary>Whether a damage component's condition is satisfied by the roll that hit.</summary>
    public static bool Applies(AttackDamageCondition? condition, AttackRoll roll)
    {
        ArgumentNullException.ThrowIfNull(roll);

        return condition switch
        {
            null => true,
            AttackDamageCondition.AttackRollHadAdvantage => roll.Roll.Mode == RollMode.Advantage,
            _ => throw new NotSupportedException($"Unhandled damage condition '{condition}'."),
        };
    }
}
