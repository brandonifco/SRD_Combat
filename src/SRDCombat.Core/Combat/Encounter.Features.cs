using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>What a Rogue may do with Cunning Action.</summary>
public enum CunningActionKind
{
    Dash,
    Disengage,
}

/// <summary>
/// The class feature actions a character can take on its turn.
/// </summary>
/// <remarks>
/// Kept in their own file because they are a distinct concern from the universal
/// actions every combatant has — and because the list will grow as more features become
/// implemented, while Move/Attack/Dodge will not.
/// </remarks>
public sealed partial class Encounter
{
    /// <summary>
    /// Barbarian Rage: a Bonus Action granting resistance to physical damage and bonus
    /// damage on Strength melee attacks.
    /// </summary>
    public ActionRefusal? Rage()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.Rage))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Rage.");
        }

        if (combatant.Features.IsRaging)
        {
            return new ActionRefusal("feature.rage.already_raging", $"{combatant.Name} is already raging.");
        }

        if (combatant.Features.RagesRemaining <= 0)
        {
            return new ActionRefusal("feature.rage.exhausted", $"{combatant.Name} has no Rages left.");
        }

        if (!combatant.Turn.HasBonusAction)
        {
            return new ActionRefusal("bonus_action.spent", $"{combatant.Name} has used its Bonus Action.");
        }

        combatant.Turn.SpendBonusAction();
        combatant.Features.RagesRemaining--;
        combatant.Features.IsRaging = true;

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} flies into a Rage! Resistance to Bludgeoning, Piercing and Slashing damage, " +
            $"and +{combatant.Stats.Character!.RageDamageBonus} damage on Strength melee attacks " +
            $"({combatant.Features.RagesRemaining} Rage(s) left).",
            combatant);

        return null;
    }

    /// <summary>Fighter Second Wind: a Bonus Action to regain 1d10 + level hit points.</summary>
    public ActionRefusal? SecondWind()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.SecondWind))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Second Wind.");
        }

        if (combatant.Features.SecondWindRemaining <= 0)
        {
            return new ActionRefusal("feature.second_wind.exhausted", $"{combatant.Name} has no uses left.");
        }

        if (!combatant.Turn.HasBonusAction)
        {
            return new ActionRefusal("bonus_action.spent", $"{combatant.Name} has used its Bonus Action.");
        }

        combatant.Turn.SpendBonusAction();
        combatant.Features.SecondWindRemaining--;

        var roll = DiceRoller.Roll(_random, new DiceExpression(1, 10, combatant.Stats.Character!.Level));
        var healed = DamageRules.Heal(combatant, roll.Total);

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} uses Second Wind: {roll} — regains {healed} hit points " +
            $"({combatant.CurrentHitPoints}/{combatant.Stats.MaximumHitPoints}).",
            combatant);

        return null;
    }

    /// <summary>Fighter Action Surge: one extra action on this turn.</summary>
    public ActionRefusal? ActionSurge()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.ActionSurge))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Action Surge.");
        }

        if (combatant.Features.ActionSurgeRemaining <= 0)
        {
            return new ActionRefusal("feature.action_surge.exhausted", $"{combatant.Name} has no uses left.");
        }

        if (combatant.Turn.HasAction)
        {
            return new ActionRefusal(
                "feature.action_surge.action_available",
                $"{combatant.Name} still has an action; Action Surge would be wasted.");
        }

        combatant.Features.ActionSurgeRemaining--;
        combatant.Turn.RestoreAction();

        Add(CombatStepKind.Feature, $"{combatant.Name} uses Action Surge and can act again.", combatant);
        return null;
    }

    /// <summary>
    /// Barbarian Reckless Attack: Advantage on Strength attacks this turn, at the cost
    /// of Advantage to everyone attacking you until your next turn.
    /// </summary>
    public ActionRefusal? RecklessAttack()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.RecklessAttack))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Reckless Attack.");
        }

        if (combatant.Features.IsRecklessThisTurn)
        {
            return new ActionRefusal("feature.reckless.already", $"{combatant.Name} is already attacking recklessly.");
        }

        combatant.Features.IsRecklessThisTurn = true;

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} attacks recklessly — Advantage on Strength attacks, and Advantage to attackers.",
            combatant);

        return null;
    }

    /// <summary>Rogue Cunning Action: Dash or Disengage as a Bonus Action.</summary>
    public ActionRefusal? CunningAction(CunningActionKind kind)
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.CunningAction))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Cunning Action.");
        }

        if (!combatant.Turn.HasBonusAction)
        {
            return new ActionRefusal("bonus_action.spent", $"{combatant.Name} has used its Bonus Action.");
        }

        combatant.Turn.SpendBonusAction();

        if (kind == CunningActionKind.Dash)
        {
            combatant.Turn.AddMovement(combatant.Stats.SpeedFeet);
            Add(
                CombatStepKind.Feature,
                $"{combatant.Name} uses Cunning Action to Dash, gaining {combatant.Stats.SpeedFeet} ft.",
                combatant);
        }
        else
        {
            combatant.Turn.Disengage();
            Add(CombatStepKind.Feature, $"{combatant.Name} uses Cunning Action to Disengage.", combatant);
        }

        return null;
    }

    /// <summary>
    /// Whether a Rogue's Sneak Attack applies to this attack.
    /// </summary>
    /// <remarks>
    /// The SRD's conditions: once per turn, with a Finesse or Ranged weapon, and either
    /// the attack has Advantage, or an ally of the target is within 5 feet of it and the
    /// attack does not have Disadvantage.
    /// </remarks>
    internal bool SneakAttackApplies(Combatant attacker, CombatAttack attack, Combatant target, AttackRoll roll)
    {
        if (!attacker.Stats.Has(ClassFeature.SneakAttack)
            || attacker.Features.SneakAttackUsedThisTurn
            || attacker.Stats.Character?.SneakAttackDamage is null)
        {
            return false;
        }

        // Every weapon a Rogue is proficient with is Finesse or Ranged, but the feature
        // is conditioned on the weapon rather than the class, so it is checked.
        var qualifies = attack.Kind == AttackKind.Ranged || attack.ReachFeet is not null;

        if (!qualifies)
        {
            return false;
        }

        if (roll.Roll.Mode == RollMode.Advantage)
        {
            return true;
        }

        if (roll.Roll.Mode == RollMode.Disadvantage)
        {
            return false;
        }

        return _combatants.Any(ally =>
            ally.Id != attacker.Id
            && ally.SideId == attacker.SideId
            && ally.IsActive
            && ally.Position.DistanceFeetTo(target.Position) <= Battlefield.FeetPerSquare);
    }

    /// <summary>
    /// Rogue Uncanny Dodge: a Reaction to halve one attack's damage.
    /// </summary>
    /// <remarks>
    /// Taken automatically rather than offered as a choice. There is no interesting
    /// decision in it at this scope — a Rogue with a spare Reaction always wants to halve
    /// incoming damage — and making the engine ask would complicate every attack for no
    /// gameplay gain. Revisit if a competing Reaction is ever implemented.
    /// </remarks>
    private bool TryUncannyDodge(Combatant target)
    {
        if (!target.Stats.Has(ClassFeature.UncannyDodge)
            || !target.Turn.HasReaction
            || !target.CanAct)
        {
            return false;
        }

        target.Turn.SpendReaction();

        Add(
            CombatStepKind.Feature,
            $"{target.Name} uses Uncanny Dodge, halving the damage.",
            target);

        return true;
    }

    /// <summary>Rage grants Resistance to Bludgeoning, Piercing and Slashing damage.</summary>
    private static bool RageResists(Combatant target, DamageType type) =>
        target.Features.IsRaging
        && type is DamageType.Bludgeoning or DamageType.Piercing or DamageType.Slashing;

    /// <summary>
    /// Ends a Rage that was not sustained. The SRD keeps a Rage going only while the
    /// Barbarian keeps fighting.
    /// </summary>
    private void EndRageIfUnsustained(Combatant combatant)
    {
        if (!combatant.Features.IsRaging || combatant.Features.AttackedThisTurn)
        {
            return;
        }

        combatant.Features.IsRaging = false;
        Add(CombatStepKind.Feature, $"{combatant.Name}'s Rage ends.", combatant);
    }
}
