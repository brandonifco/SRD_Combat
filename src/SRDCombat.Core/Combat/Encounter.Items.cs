using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// Using the consumables a creature carried into the fight.
/// </summary>
public sealed partial class Encounter
{
    /// <summary>
    /// Drinks a Potion of Healing, or administers one to a creature within reach.
    /// </summary>
    /// <param name="potency">Which potion to spend.</param>
    /// <param name="target">
    /// Who drinks it. Null is the actor itself, which is the ordinary case.
    /// </param>
    /// <remarks>
    /// <para>
    /// "Drinking a potion or administering it to another creature requires a Bonus
    /// Action" — the same cost either way, which is the whole reason this action is
    /// interesting: a character can spend one Bonus Action to put an Unconscious ally
    /// back on their feet without touching their own Action, and
    /// <c>Combatant.RegainHitPoints</c> already clears the dying state, the Death Saving
    /// Throws and Unconscious when it takes anyone above 0.
    /// </para>
    /// <para>
    /// Every refusal here happens <em>before</em> the potion is spent. Wasting one on a
    /// corpse or on somebody out of reach is exactly the mistake a refusal should catch,
    /// and a consumable spent wrongly cannot be given back the way a spent Action can be
    /// re-decided in a client.
    /// </para>
    /// </remarks>
    public ActionRefusal? DrinkPotion(HealingPotion potency, Combatant? target = null)
    {
        if (ActiveCombatant is not { } actor)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!actor.CanAct)
        {
            return new ActionRefusal("combatant.cannot_act", $"{actor.Name} cannot act.");
        }

        var drinker = target ?? actor;

        if (actor.Inventory.CountOf(potency) <= 0)
        {
            return new ActionRefusal(
                "potion.none",
                $"{actor.Name} carries no {PotionRules.PrintedName(potency)}.");
        }

        if (!actor.Turn.HasBonusAction)
        {
            return new ActionRefusal("bonus_action.spent", $"{actor.Name} has used its Bonus Action.");
        }

        if (drinker.IsDead)
        {
            return new ActionRefusal(
                "potion.target_dead",
                $"{drinker.Name} is dead; a potion cannot help.");
        }

        // The SRD sets no range on administering; this engine reads it as needing reach,
        // stated on PotionRules.ReachFeet.
        var distance = actor.Position.DistanceFeetTo(drinker.Position);

        if (!ReferenceEquals(drinker, actor) && distance > PotionRules.ReachFeet)
        {
            return new ActionRefusal(
                "potion.out_of_reach",
                $"{drinker.Name} is {distance} feet away; administering a potion needs " +
                $"{PotionRules.ReachFeet} feet.");
        }

        actor.Turn.SpendBonusAction();
        actor.Inventory.Spend(potency);

        var rolled = DiceRoller.Roll(_random, PotionRules.Healing(potency));
        var wasDown = drinker.CurrentHitPoints == 0;
        var restored = DamageRules.Heal(drinker, rolled.Total);

        var opening = ReferenceEquals(drinker, actor)
            ? $"{actor.Name} drinks a {PotionRules.PrintedName(potency)}"
            : $"{actor.Name} administers a {PotionRules.PrintedName(potency)} to {drinker.Name}";

        Add(
            CombatStepKind.Item,
            $"{opening}: [{rolled}] — regains {restored} hit points, {DescribeHealth(drinker)}." +
            (wasDown && drinker.CurrentHitPoints > 0 ? $" {drinker.Name} is back on their feet." : string.Empty),
            actor,
            ReferenceEquals(drinker, actor) ? null : drinker);

        return null;
    }
}
