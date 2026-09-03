using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// One item a live <see cref="Encounter"/> is asked to move from the active combatant to
/// an ally — a closed set, so a request the engine cannot honour is refused with a named
/// code rather than silently going nowhere.
/// </summary>
/// <remarks>
/// <b>Only <see cref="Potion"/> can actually move.</b> <see cref="Gear"/> exists so
/// <see cref="Encounter.TradeItem"/> can answer an attempted in-fight weapon, armour,
/// shield or magic-item transfer honestly (<c>trade.gear_in_fight</c>) instead of the
/// request simply having no case to reach. Equipment is a <c>CharacterDraft</c> choice
/// that <c>CharacterResolver</c> turns into derived numbers, and no live
/// <see cref="Encounter"/> supports re-resolving two combatants mid-fight — see #536's
/// design record for why that stays out of this slice.
/// </remarks>
public abstract record CombatTradeItem
{
    private CombatTradeItem()
    {
    }

    /// <summary>A Potion of Healing, of the given potency.</summary>
    public sealed record Potion(HealingPotion Potency) : CombatTradeItem;

    /// <summary>
    /// A request to trade a piece of gear. Always refused in a live encounter;
    /// <paramref name="ItemId"/> is carried for the refusal's own record only and is not
    /// read by any transfer.
    /// </summary>
    public sealed record Gear(string ItemId) : CombatTradeItem;
}

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
    /// <para>
    /// <b>A potion within reach is usable, whoever is carrying it.</b> The flask may come
    /// from the drinker's own pack as readily as the actor's, and the drinker's is
    /// reached for first — spending someone's own potion on them before opening your
    /// pack is what a person does, and it leaves the helper's supplies intact. Until a
    /// played run found it, administering read only the actor's inventory, which meant a
    /// potion found by a character who later went down was stuck with them: the one
    /// person who could not act was the only one who could drink it.
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

        // Whose flask this is. The drinker's own comes first; the actor's pack is the
        // fallback, and for a creature drinking its own potion the two are the same.
        var carrier = drinker.Inventory.CountOf(potency) > 0 ? drinker : actor;

        if (carrier.Inventory.CountOf(potency) <= 0)
        {
            return new ActionRefusal(
                "potion.none",
                ReferenceEquals(drinker, actor)
                    ? $"{actor.Name} carries no {PotionRules.PrintedName(potency)}."
                    : $"Neither {actor.Name} nor {drinker.Name} carries a {PotionRules.PrintedName(potency)}.");
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
        var distance = actor.DistanceFeetTo(drinker);

        if (!ReferenceEquals(drinker, actor) && distance > PotionRules.ReachFeet)
        {
            return new ActionRefusal(
                "potion.out_of_reach",
                $"{drinker.Name} is {distance} feet away; administering a potion needs " +
                $"{PotionRules.ReachFeet} feet.");
        }

        actor.Turn.SpendBonusAction();
        carrier.Inventory.Spend(potency);

        var rolled = DiceRoller.Roll(_random, PotionRules.Healing(potency));
        var wasDown = drinker.CurrentHitPoints == 0;
        var restored = DamageRules.Heal(drinker, rolled.Total);

        // Whose flask it was is worth narrating: "their own" is the difference between
        // a rescuer spending their supplies and spending the casualty's.
        var whose = ReferenceEquals(carrier, drinker) ? "their own " : string.Empty;

        var opening = ReferenceEquals(drinker, actor)
            ? $"{actor.Name} drinks a {PotionRules.PrintedName(potency)}"
            : $"{actor.Name} administers {whose}{PotionRules.PrintedName(potency)} to {drinker.Name}";

        Add(
            CombatStepKind.Item,
            $"{opening}: [{rolled}] — regains {restored} hit points, {DescribeHealth(drinker)}." +
            (wasDown && drinker.CurrentHitPoints > 0 ? $" {drinker.Name} is back on their feet." : string.Empty),
            actor,
            ReferenceEquals(drinker, actor) ? null : drinker);

        return null;
    }

    /// <summary>
    /// Hands one item from the active combatant to an ally within reach.
    /// </summary>
    /// <param name="item">What is offered. Only <see cref="CombatTradeItem.Potion"/> can
    /// actually move; a <see cref="CombatTradeItem.Gear"/> request is refused outright.</param>
    /// <param name="recipient">Who receives it.</param>
    /// <remarks>
    /// <para>
    /// <b>Free, but only once a turn.</b> The SRD prints no combat cost for handing an
    /// item to another creature; this engine's reading — stated rather than a printed
    /// rule — is that one trade a turn costs neither the Action nor the Bonus Action, and
    /// <see cref="TurnResources.HasTradeInteraction"/> is the resource that enforces the
    /// "once" half. Only the giver spends it; the recipient pays nothing, conscious or
    /// not. See #536's design record for the reasoning.
    /// </para>
    /// <para>
    /// <b>The direction is always giver to recipient.</b> Unlike <see cref="DrinkPotion"/>,
    /// this never falls back to the recipient's own pack — a trade moves what the actor
    /// carries, so a potion the recipient already has of their own is untouched.
    /// </para>
    /// <para>
    /// <b>Refusals are ordered deliberately</b> — the unsupported-gear check first (a
    /// categorical limit, independent of any target), then everything about whether
    /// <paramref name="recipient"/> is a legal recipient at all, then whether the actor
    /// actually carries the requested potion, and only last whether this turn's trade is
    /// already spent. That makes a badly chosen target visible before an affordability
    /// question that never mattered: a request naming a missing potion for somebody ten
    /// feet away reports the distance, not the missing flask, because moving the target
    /// into reach is the more useful next fact.
    /// </para>
    /// <para>
    /// Nothing changes until every check passes — the same discipline
    /// <see cref="DrinkPotion"/> holds, because a consumable handed to the wrong person
    /// cannot be handed back.
    /// </para>
    /// </remarks>
    public ActionRefusal? TradeItem(CombatTradeItem item, Combatant recipient)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(recipient);

        if (ActiveCombatant is not { } actor)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!actor.CanAct)
        {
            return new ActionRefusal("combatant.cannot_act", $"{actor.Name} cannot act.");
        }

        if (item is not CombatTradeItem.Potion potion)
        {
            return new ActionRefusal("trade.gear_in_fight", "Gear cannot be transferred during a fight.");
        }

        if (!_combatants.Any(candidate => ReferenceEquals(candidate, recipient)))
        {
            return new ActionRefusal(
                "trade.target_not_present",
                $"{recipient.Name} is not in this encounter.");
        }

        if (ReferenceEquals(recipient, actor))
        {
            return new ActionRefusal(
                "trade.same_carrier",
                $"{actor.Name} already carries that item.");
        }

        if (recipient.SideId != actor.SideId)
        {
            return new ActionRefusal(
                "trade.target_not_ally",
                $"{recipient.Name} is not an ally of {actor.Name}.");
        }

        if (recipient.IsDead)
        {
            return new ActionRefusal(
                "trade.target_dead",
                $"{recipient.Name} is dead and cannot receive an item.");
        }

        var distance = actor.DistanceFeetTo(recipient);

        if (distance > PotionRules.ReachFeet)
        {
            return new ActionRefusal(
                "trade.out_of_reach",
                $"{recipient.Name} is {distance} feet away; trading an item needs " +
                $"{PotionRules.ReachFeet} feet.");
        }

        if (actor.Inventory.CountOf(potion.Potency) <= 0)
        {
            return new ActionRefusal(
                "trade.item_missing",
                $"{actor.Name} carries no {PotionRules.PrintedName(potion.Potency)}.");
        }

        if (!actor.Turn.HasTradeInteraction)
        {
            return new ActionRefusal(
                "trade.already_used",
                $"{actor.Name} has already traded an item this turn.");
        }

        actor.Turn.SpendTradeInteraction();
        actor.Inventory.Spend(potion.Potency);
        recipient.Inventory.Add(potion.Potency);

        Add(
            CombatStepKind.Item,
            $"{actor.Name} gives a {PotionRules.PrintedName(potion.Potency)} to {recipient.Name}.",
            actor,
            recipient);

        return null;
    }
}
