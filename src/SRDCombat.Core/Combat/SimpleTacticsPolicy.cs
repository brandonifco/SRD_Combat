using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// Takes a combatant's whole turn: close with the nearest enemy and hit it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately unsophisticated. Real monster tactics — focus fire, using the action
/// economy properly, positioning, retreating — are their own phase of work. What this
/// exists for is to drive a fight from start to finish without a client, which is what
/// makes an end-to-end engine test possible at all.
/// </para>
/// <para>
/// It is nonetheless fully deterministic: every tie is broken by an explicit ordering
/// rather than by enumeration order, so the same seed always produces the same fight.
/// Without that the frozen transcripts would be flaky.
/// </para>
/// </remarks>
public static class SimpleTacticsPolicy
{
    /// <summary>Plays out the active combatant's turn and ends it.</summary>
    public static void TakeTurn(Encounter encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);

        if (encounter.IsComplete || encounter.ActiveCombatant is not { } actor)
        {
            return;
        }

        if (!actor.CanAct)
        {
            encounter.EndTurn();
            return;
        }

        if (actor.HasCondition(ConditionType.Prone))
        {
            encounter.StandUp();
        }

        var target = NearestEnemy(encounter, actor);

        if (target is null)
        {
            encounter.EndTurn();
            return;
        }

        // Attack from where we stand if anything reaches.
        if (TryAttack(encounter, actor, target))
        {
            SpendRemainingAttacks(encounter, actor);
            encounter.EndTurn();
            return;
        }

        MoveTowards(encounter, actor, target);

        // The move may have provoked an Opportunity Attack that dropped us, or ended the
        // fight outright, so re-check before swinging.
        if (encounter.IsComplete || !actor.CanAct)
        {
            encounter.EndTurn();
            return;
        }

        var closest = NearestEnemy(encounter, actor);

        if (closest is not null && TryAttack(encounter, actor, closest))
        {
            SpendRemainingAttacks(encounter, actor);
        }

        encounter.EndTurn();
    }

    /// <summary>Runs the whole fight, stopping if it somehow fails to resolve.</summary>
    /// <param name="encounter">The fight to run.</param>
    /// <param name="roundLimit">
    /// A guard against a fight that cannot end — two creatures that can never reach or
    /// hurt each other would otherwise loop forever.
    /// </param>
    public static void RunToCompletion(Encounter encounter, int roundLimit = 50)
    {
        ArgumentNullException.ThrowIfNull(encounter);

        while (!encounter.IsComplete && encounter.Round <= roundLimit)
        {
            TakeTurn(encounter);
        }
    }

    /// <summary>
    /// Uses the rest of the swings an Attack action bought, from Extra Attack or a
    /// Multiattack. Retargets between swings, so a creature that kills its target does
    /// not waste the remainder on a corpse.
    /// </summary>
    private static void SpendRemainingAttacks(Encounter encounter, Combatant actor)
    {
        while (!encounter.IsComplete
               && actor.CanAct
               && actor.Features.AttacksRemainingThisAction > 0
               && NearestEnemy(encounter, actor) is { } next
               && TryAttack(encounter, actor, next))
        {
            // TryAttack consumes one swing per call.
        }
    }

    private static Combatant? NearestEnemy(Encounter encounter, Combatant actor) =>
        encounter.EnemiesOf(actor)
            .OrderBy(enemy => actor.Position.DistanceFeetTo(enemy.Position))
            .ThenBy(enemy => enemy.CurrentHitPoints)
            .ThenBy(enemy => enemy.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Attacks with the hardest-hitting attack that can reach the target.</summary>
    private static bool TryAttack(Encounter encounter, Combatant actor, Combatant target)
    {
        if (!actor.Turn.HasAction && actor.Features.AttacksRemainingThisAction <= 0)
        {
            return false;
        }

        var distance = actor.Position.DistanceFeetTo(target.Position);

        var attack = actor.Stats.Attacks
            .Where(candidate => candidate.CanReach(distance))
            .Where(candidate => actor.Stats.AllowsInMultiattack(candidate.Name))
            .OrderByDescending(candidate => candidate.Damage.Sum(damage => damage.Amount.Average))
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return attack is not null && encounter.Attack(attack.Name, target) is null;
    }

    /// <summary>
    /// Moves as close to the target as this turn's movement allows, preferring a square
    /// the creature can actually attack from.
    /// </summary>
    private static void MoveTowards(Encounter encounter, Combatant actor, Combatant target)
    {
        var reach = actor.Stats.Attacks.Count > 0
            ? actor.Stats.Attacks.Max(attack => attack.MaximumRangeFeet)
            : MovementRules.MeleeReachFeet(actor);

        var candidates = encounter.Battlefield.AllSquares()
            .Where(square => MovementRules.FindPath(
                encounter.Battlefield,
                actor,
                square,
                actor.Turn.MovementFeet,
                encounter.Combatants) is not null)
            .Select(square => new
            {
                Square = square,
                Distance = square.DistanceFeetTo(target.Position),
            })
            .Where(option => option.Distance < actor.Position.DistanceFeetTo(target.Position))
            .OrderBy(option => option.Distance > reach ? 1 : 0)
            .ThenBy(option => option.Distance)
            .ThenBy(option => option.Square.X)
            .ThenBy(option => option.Square.Y)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        encounter.Move(candidates[0].Square);
    }
}
