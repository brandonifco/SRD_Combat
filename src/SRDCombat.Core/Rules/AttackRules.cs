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
/// <param name="TargetIsPetrified">
/// The target is Petrified. Advantage only — the glossary prints no Critical Hit
/// clause for stone, where Paralyzed and Unconscious both carry one.
/// </param>
/// <param name="RangedAttackInCloseCombat">
/// The attack rolls ranged while an able enemy stands within 5 feet. Printed page 15:
/// "When you make a ranged attack roll with a weapon, a spell, or some other means, you
/// have Disadvantage on the roll if you are within 5 feet of an enemy who can see you
/// and doesn't have the Incapacitated condition." Any enemy counts, the target included.
/// "Who can see you" rests on the same stated reading Frightened records: sight is
/// unmodelled, so an enemy on the field is read as seeing the attacker unless it has the
/// Blinded condition — the one part of sight the engine does express.
/// </param>
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
    bool TargetIsStunned = false,
    bool RangedAttackInCloseCombat = false,
    bool TargetIsPetrified = false);

/// <summary>The outcome of one attack roll, before damage is applied.</summary>
/// <param name="Roll">The d20 roll.</param>
/// <param name="Hit">Whether it hit.</param>
/// <param name="Critical">Whether it is a Critical Hit.</param>
/// <param name="TargetArmorClass">
/// The AC it was rolled against — the target's own plus any cover bonus, because that
/// sum is the number the roll actually had to meet and the number narration should say.
/// </param>
/// <param name="Circumstances">What produced the roll's mode.</param>
/// <param name="Cover">The degree of cover the target had, for narration.</param>
public sealed record AttackRoll(
    D20Roll Roll,
    bool Hit,
    bool Critical,
    int TargetArmorClass,
    AttackCircumstances Circumstances,
    CoverDegree Cover = CoverDegree.None);

/// <summary>Resolving attack rolls.</summary>
public static class AttackRules
{
    /// <summary>Works out the Advantage and Disadvantage applying to an attack.</summary>
    /// <param name="combatants">
    /// Everyone on the field, for the circumstances that depend on more than the two
    /// creatures involved — today that is Ranged Attacks in Close Combat, which asks
    /// about <em>any</em> enemy within 5 feet. Null means the caller has no field to
    /// offer (a two-creature unit test), and those circumstances stay false.
    /// </param>
    public static AttackCircumstances DescribeCircumstances(
        Combatant attacker,
        CombatAttack attack,
        Combatant target,
        IReadOnlyCollection<Combatant>? combatants = null)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(target);

        var distance = attacker.Position.DistanceFeetTo(target.Position);

        return new AttackCircumstances(
            // Dodge is lost while Incapacitated or at Speed 0 (the printed exception,
            // shared with the Dexterity-save half through RetainsDodgeBenefits), and its
            // attack-roll half alone is gated on "if you can see the attacker" — read as
            // the dodger not being Blinded, the same sight reading Ranged Attacks in
            // Close Combat records.
            TargetIsDodging: target.Turn.IsDodging
                && ConditionRules.RetainsDodgeBenefits(target)
                && !target.HasCondition(ConditionType.Blinded),
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
            TargetIsStunned: target.HasCondition(ConditionType.Stunned),
            TargetIsPetrified: target.HasCondition(ConditionType.Petrified),
            RangedAttackInCloseCombat:
                combatants is not null && InCloseCombat(attacker, attack, distance, combatants));
    }

    /// <summary>
    /// Whether this ranged attack roll is being made within 5 feet of an able enemy.
    /// </summary>
    /// <remarks>
    /// The enemy must be alive (a corpse is not an enemy who can see anything), able to
    /// see the attacker (read as: not Blinded — the reading is on
    /// <see cref="AttackCircumstances.RangedAttackInCloseCombat"/>), and not
    /// Incapacitated, which Unconscious and Paralyzed both bring with them.
    /// </remarks>
    private static bool InCloseCombat(
        Combatant attacker,
        CombatAttack attack,
        int distanceFeet,
        IReadOnlyCollection<Combatant> combatants)
    {
        if (!attack.IsRangedAttackRoll(distanceFeet))
        {
            return false;
        }

        return combatants.Any(other =>
            other.SideId != attacker.SideId
            && !other.IsDead
            && !other.HasCondition(ConditionType.Blinded)
            && !other.HasCondition(ConditionType.Incapacitated)
            && other.Position.DistanceFeetTo(attacker.Position) <= Battlefield.FeetPerSquare);
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
            || circumstances.TargetIsPetrified
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
            || circumstances.RangedAttackInCloseCombat
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
    /// <param name="combatants">
    /// Everyone on the field, when the caller has it — see
    /// <see cref="DescribeCircumstances"/>.
    /// </param>
    /// <param name="cover">
    /// The target's cover against this attack, already judged by the caller —
    /// <see cref="CoverRules.Between"/> needs the battlefield, which this method never
    /// sees. Half and Three-Quarters raise the AC to beat; Total never reaches here,
    /// because "can't be targeted directly" is a refusal before anything is rolled.
    /// </param>
    public static AttackRoll Resolve(
        IRandomSource random,
        Combatant attacker,
        CombatAttack attack,
        Combatant target,
        bool extraAdvantage = false,
        bool extraDisadvantage = false,
        IReadOnlyCollection<Combatant>? combatants = null,
        CoverDegree cover = CoverDegree.None)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(target);

        var distance = attacker.Position.DistanceFeetTo(target.Position);
        var circumstances = DescribeCircumstances(attacker, attack, target, combatants);
        var mode = D20Test.Combine(ResolveRollMode(circumstances, distance), extraAdvantage, extraDisadvantage);

        var armorClass = target.Stats.ArmorClass + CoverRules.Bonus(cover);

        var roll = D20Test.Roll(random, attack.AttackBonus, mode);

        // A natural 20 always hits and is always a Critical Hit; a natural 1 always
        // misses. Both ignore modifiers and AC entirely — cover included: the bonus
        // raises the number to beat, and a natural 20 does not beat numbers.
        if (roll.IsNatural1)
        {
            return new AttackRoll(roll, Hit: false, Critical: false, armorClass, circumstances, cover);
        }

        var hit = roll.IsNatural20 || roll.Total >= armorClass;

        // Improved Critical widens the crit band and nothing else — a natural 19 still
        // has to beat AC, because only the 20 auto-hits. Unconscious and Paralyzed print
        // the same clause as each other: any hit from within 5 feet is a Critical Hit.
        // Stunned deliberately grants neither — the glossary gives it Advantage against
        // and nothing more.
        var critical = roll.IsNatural20
            || (hit && roll.Natural >= attacker.Stats.CriticalHitThreshold)
            || (hit
                && (circumstances.TargetIsUnconscious || circumstances.TargetIsParalyzed)
                && distance <= Battlefield.FeetPerSquare);

        // Adamantine Armor: "any Critical Hit against you becomes a normal hit" — any,
        // so a natural 20 still hits (that clause is about hitting, not the crit) but
        // its dice are not doubled, and the condition-granted crits above are denied
        // the same way.
        critical &= !target.Stats.CriticalHitsAgainstBecomeNormal;

        return new AttackRoll(roll, hit, critical, armorClass, circumstances, cover);
    }

    /// <summary>
    /// Rolls an attack's damage, doubling the dice on a Critical Hit and skipping any
    /// component whose condition is not met.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The conditional case is rare but real, and getting it wrong is invisible: a
    /// Goblin Warrior deals "plus 2 (1d4) Slashing damage if the attack roll had
    /// Advantage", and applying that unconditionally makes it hit for half again as much
    /// as the SRD says on every ordinary swing.
    /// </para>
    /// <para>
    /// <see cref="CombatAttack.Alternative"/> (#371) is checked first and, when its own
    /// condition holds, <em>replaces</em> <paramref name="attack"/>'s whole
    /// <see cref="CombatAttack.Damage"/> rather than joining it — the printed "or"
    /// this models is the opposite grammar from "plus". A Bloodied swarm's bite deals
    /// its lower alternative, never its base damage plus the alternative.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(AttackDamage Component, DiceRollResult Result)> RollDamage(
        IRandomSource random,
        CombatAttack attack,
        AttackRoll roll,
        Combatant attacker,
        Combatant target)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(roll);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        var damage = attack.Alternative is { } alternative
            && Applies(alternative.Condition, roll, attacker, target)
                ? [new AttackDamage(alternative.Amount, alternative.Type, alternative.PrintedAverage)]
                : attack.Damage;

        return damage
            .Where(component => Applies(component.Condition, roll, attacker, target))
            .Select(component => (component, DiceRoller.Roll(random, component.Amount, roll.Critical)))
            .ToArray();
    }

    /// <summary>Whether a damage component's condition is satisfied by the roll that hit.</summary>
    public static bool Applies(AttackDamageCondition? condition, AttackRoll roll, Combatant attacker, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(roll);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        return condition switch
        {
            null => true,
            AttackDamageCondition.AttackRollHadAdvantage => roll.Roll.Mode == RollMode.Advantage,
            AttackDamageCondition.AttackerIsBloodied => attacker.IsBloodied,
            AttackDamageCondition.TargetIsBloodied => target.IsBloodied,
            _ => throw new NotSupportedException($"Unhandled damage condition '{condition}'."),
        };
    }
}
