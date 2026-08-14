using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// The party's shared judgement: what the squad, rather than the soldier, would decide.
/// </summary>
/// <remarks>
/// <para>
/// Squad AI slice 2 (#123). The squad-tactics decomposition this series follows is
/// squad intent → roles → positioning, and this class is the first rung: a place for
/// decisions that belong to the side rather than to the actor. It holds no state — every
/// answer is a pure function of the encounter, recomputed on demand — because the
/// frozen transcripts rest on determinism, and a blackboard that remembered things
/// would be a second copy of the fight to keep honest.
/// </para>
/// <para>
/// <b>Only characters consult it.</b> The gate is <c>Stats.Character</c>, the same line
/// the policy already draws for features and healing: a monster's turn stays the simple
/// policy's until the Phase 6 split (#127) gives monsters doctrine of their own, and
/// the hand-authored transcript combatants carry no character block, so the fixture is
/// untouched by construction.
/// </para>
/// </remarks>
public static class PartyDoctrine
{
    /// <summary>
    /// The enemy the whole side should be killing: the most threat per hit point left.
    /// </summary>
    /// <remarks>
    /// Focus fire works because a dead enemy loses its whole action economy — killing
    /// one removes its attacks permanently; wounding five removes nothing — so the
    /// shared target is the one whose death buys the most safety soonest:
    /// <see cref="ThreatPerRound"/> divided by the hit points still to chew through.
    /// Ties break on lower hit points, then identifier, so the same seed always
    /// produces the same fight.
    /// </remarks>
    public static Combatant? FocusTarget(Encounter encounter, Combatant actor)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        return encounter.EnemiesOf(actor)
            .Where(enemy => !enemy.IsDead)
            .OrderByDescending(enemy => ThreatPerRound(enemy) / Math.Max(1, enemy.CurrentHitPoints))
            .ThenBy(enemy => enemy.CurrentHitPoints)
            .ThenBy(enemy => enemy.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Roughly how much damage this creature deals per round: its hardest attack's
    /// average times the swings its action buys.
    /// </summary>
    /// <remarks>
    /// An estimate for ordering, not a simulation — the same altitude the attack
    /// chooser and the Opportunity-Attack cost already work at. Entries, spells and
    /// riders are not counted; when a creature's real menace lives outside its attack
    /// line, this underrates it, and the number is only ever compared against other
    /// numbers made the same way.
    /// </remarks>
    public static double ThreatPerRound(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        var hardest = combatant.Stats.Attacks
            .Select(attack => attack.Damage.Sum(damage => damage.Amount.Average))
            .DefaultIfEmpty(0)
            .Max();

        var swings = combatant.Stats.Multiattack?.AttackCount
            ?? combatant.Stats.Character?.AttacksPerAction
            ?? 1;

        return hardest * swings;
    }

    /// <summary>
    /// The target a character should fight: the side's focus target when this character
    /// can attack it from where it stands, its own best reachable enemy when it cannot
    /// but something else is in reach — a turn in reach of an enemy is never spent
    /// walking — and the focus target again when nothing is in reach at all, so the
    /// whole side converges on the same kill.
    /// </summary>
    public static Combatant? ChooseTarget(Encounter encounter, Combatant actor, Combatant? nearest)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.Stats.Character is null)
        {
            return nearest;
        }

        if (FocusTarget(encounter, actor) is not { } focus)
        {
            return nearest;
        }

        var distance = actor.Position.DistanceFeetTo(focus.Position);
        var attackableNow = actor.Stats.Attacks.Any(attack => attack.CanReach(distance))
            && CoverRules.Between(encounter.Battlefield, actor.Position, focus.Position, encounter.Combatants)
                != CoverDegree.Total;

        if (attackableNow)
        {
            return focus;
        }

        var somethingInReach = nearest is not null
            && actor.Stats.Attacks.Any(attack =>
                attack.CanReach(actor.Position.DistanceFeetTo(nearest.Position)));

        return somethingInReach ? nearest : focus;
    }
}
