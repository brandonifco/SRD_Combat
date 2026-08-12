using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>A route across the grid and what it costs.</summary>
/// <param name="Steps">The squares entered, in order. Does not include the starting square.</param>
/// <param name="CostFeet">Total movement cost in feet.</param>
public sealed record MovementPath(IReadOnlyList<GridPosition> Steps, int CostFeet);

/// <summary>Moving around the battlefield.</summary>
public static class MovementRules
{
    /// <summary>
    /// Finds the cheapest route from one square to another within a movement budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uniform-cost search rather than A*: the grids here are small, and difficult
    /// terrain makes step costs non-uniform, so the simpler algorithm is both correct
    /// and fast enough.
    /// </para>
    /// <para>
    /// Occupancy follows the SRD with one deliberate simplification. A creature may move
    /// through an ally's space, which counts as Difficult Terrain, and may never end its
    /// move in an occupied square. Moving through a <em>hostile</em> creature's space is
    /// treated as impossible; RAW allows it when the creatures differ by two size
    /// categories, which is not modelled yet.
    /// </para>
    /// </remarks>
    public static MovementPath? FindPath(
        Battlefield field,
        Combatant mover,
        GridPosition destination,
        int budgetFeet,
        IReadOnlyCollection<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(combatants);

        if (destination == mover.Position || !field.IsPassable(destination))
        {
            return null;
        }

        var blockers = combatants
            .Where(other => other.Id != mover.Id && other.IsActive)
            .ToDictionary(other => other.Position, other => other.SideId);

        if (blockers.ContainsKey(destination))
        {
            return null;
        }

        var best = new Dictionary<GridPosition, int> { [mover.Position] = 0 };
        var cameFrom = new Dictionary<GridPosition, GridPosition>();
        var queue = new PriorityQueue<GridPosition, int>();
        queue.Enqueue(mover.Position, 0);

        while (queue.TryDequeue(out var current, out var cost))
        {
            if (cost > best.GetValueOrDefault(current, int.MaxValue))
            {
                continue;
            }

            if (current == destination)
            {
                return new MovementPath(Reconstruct(cameFrom, mover.Position, destination), cost);
            }

            foreach (var next in current.Neighbours())
            {
                if (!field.IsPassable(next))
                {
                    continue;
                }

                if (blockers.TryGetValue(next, out var sideId))
                {
                    // Never end on someone; only pass through an ally.
                    if (next == destination || sideId != mover.SideId)
                    {
                        continue;
                    }
                }

                // An occupied square costs the same as Difficult Terrain to cross.
                var stepCost = blockers.ContainsKey(next)
                    ? Battlefield.FeetPerSquare * 2
                    : field.EnterCostFeet(next);

                var total = cost + stepCost;

                if (total > budgetFeet || total >= best.GetValueOrDefault(next, int.MaxValue))
                {
                    continue;
                }

                best[next] = total;
                cameFrom[next] = current;
                queue.Enqueue(next, total);
            }
        }

        return null;
    }

    /// <summary>
    /// The furthest a creature can reach with a melee attack, in feet. Used to decide
    /// whether movement provokes an Opportunity Attack.
    /// </summary>
    public static int MeleeReachFeet(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        var reaches = combatant.Stats.Attacks
            .Where(attack => attack.Kind == AttackKind.Melee && attack.ReachFeet is not null)
            .Select(attack => attack.ReachFeet!.Value)
            .ToArray();

        return reaches.Length > 0 ? reaches.Max() : Battlefield.FeetPerSquare;
    }

    /// <summary>
    /// Finds the enemies who get an Opportunity Attack when the mover steps from one
    /// square to another.
    /// </summary>
    /// <remarks>
    /// The trigger is precise: the creature must <em>leave</em> the enemy's reach, so an
    /// enemy who was already out of reach, or who is still in reach after the step, gets
    /// nothing. Taking the Disengage action avoids provoking entirely, and an enemy who
    /// cannot act or has already spent its Reaction cannot make one.
    /// </remarks>
    public static IReadOnlyList<Combatant> FindOpportunityAttackers(
        Combatant mover,
        GridPosition from,
        GridPosition to,
        IReadOnlyCollection<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(combatants);

        if (mover.Turn.HasDisengaged)
        {
            return [];
        }

        // Flyby: "doesn't provoke Opportunity Attacks when it flies out of an enemy's
        // reach." The engine has no movement modes, so a creature printed with Flyby is
        // read as flying whenever it moves — the reading is on MonsterTraitRegistry.
        if (mover.HasTrait(MonsterTrait.Flyby))
        {
            return [];
        }

        return combatants
            .Where(enemy => enemy.SideId != mover.SideId)
            .Where(enemy => enemy.IsActive && enemy.Turn.HasReaction)
            .Where(enemy =>
            {
                var reach = MeleeReachFeet(enemy);
                var wasInReach = enemy.Position.DistanceFeetTo(from) <= reach;
                var stillInReach = enemy.Position.DistanceFeetTo(to) <= reach;

                return wasInReach && !stillInReach;
            })
            .ToArray();
    }

    /// <summary>The movement cost of standing up from Prone: half the creature's Speed, rounded down.</summary>
    public static int StandUpCostFeet(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        // Rounded down to a whole number of feet, then to whole squares is not required
        // by the rules — the SRD says half your Speed, and a 25 ft. Speed gives 12.
        return combatant.Stats.SpeedFeet / 2;
    }

    private static List<GridPosition> Reconstruct(
        Dictionary<GridPosition, GridPosition> cameFrom,
        GridPosition start,
        GridPosition destination)
    {
        var steps = new List<GridPosition>();
        var current = destination;

        while (current != start)
        {
            steps.Add(current);
            current = cameFrom[current];
        }

        steps.Reverse();
        return steps;
    }
}
