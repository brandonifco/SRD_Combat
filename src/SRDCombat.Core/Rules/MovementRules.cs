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
    /// <b>Equal-cost routes are not equally good to look at, so ties are broken.</b>
    /// Every square costs the same five feet, diagonals included, which means a route
    /// may drift a row out of its way and back for free whenever the other axis is the
    /// one deciding the distance: <c>(1,2)</c> to <c>(6,1)</c> came out as
    /// <c>(2,1) (3,0) (4,1) (5,1) (6,1)</c>, five steps and twenty-five feet like the
    /// straight one, but it visibly strolls up to the top row and comes back. The cost
    /// was right and the picture was wrong, and on a board where a token now walks its
    /// route rather than appearing at the end of it, the picture is what a player reads.
    /// So the search carries a second key — how many times a step moves *away* from the
    /// destination on an axis — and prefers the route that never does. It orders equal
    /// costs and can never beat cost, so what comes back is still the cheapest way there.
    /// </para>
    /// <para>
    /// Occupancy follows the printed <em>Moving around Other Creatures</em> rule: "you
    /// can pass through the space of an ally, a creature that has the Incapacitated
    /// condition, a Tiny creature, or a creature that is two sizes larger or smaller
    /// than you", "another creature's space is Difficult Terrain for you unless that
    /// creature is Tiny or your ally", and "you can't willingly end a move in a space
    /// occupied by another creature".
    /// </para>
    /// <para>
    /// Two of the four pass-through clauses are modelled: <b>ally</b> and
    /// <b>Incapacitated</b>. The Incapacitated clause names no side, so a downed
    /// <em>enemy</em> is walked through exactly like a downed friend — which is the
    /// point of it. Without that, a body dropped in a doorway walls a side off, and the
    /// tactics policy's stuck-turn rule was carrying the workaround for a gap that
    /// belonged here. <b>Tiny</b> and <b>two size categories apart</b> stay unmodelled
    /// and so still block; both would only widen what is passable, never narrow it.
    /// </para>
    /// <para>
    /// The Difficult Terrain clause exempts allies, so squeezing past your own front
    /// line costs the ordinary five feet. Everyone else's space — a downed enemy's
    /// included — costs double, and never more: difficult terrain does not stack with
    /// itself, so an occupied square that is <em>also</em> rough ground is still just
    /// double.
    /// </para>
    /// <para>
    /// Ending a move on anyone stays refused, which is what makes the wake-up case
    /// impossible rather than merely unlikely: if nobody may finish a turn standing on
    /// a downed creature, no downed creature can regain consciousness underneath one,
    /// and no displacement rule is needed. The print's own answer for a shared square
    /// arrived at some other way is the Prone condition, not a shove to the nearest
    /// free square.
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

        // Anyone still on the field occupies their square, conscious or not. Reading it
        // as "active" let a creature end its move on a downed one — harmless until
        // healing arrived, at which point the downed creature stood up inside somebody
        // else and the next path finder found two combatants in one square.
        //
        // Keyed as a lookup rather than a dictionary for the same reason: two creatures
        // sharing a square is a state this method must survive rather than throw on,
        // whatever produced it. Every occupant of a square is consulted, so a square
        // holding both a friend and a stranger is judged by the stranger.
        var occupants = combatants
            .Where(other => other.Id != mover.Id && !other.IsDead)
            .ToLookup(other => other.Position);

        // "You can't willingly end a move in a space occupied by another creature" —
        // whoever it is, however incapable they are.
        if (occupants.Contains(destination))
        {
            return null;
        }

        var start = (Cost: 0, Wandered: 0);
        var best = new Dictionary<GridPosition, (int Cost, int Wandered)> { [mover.Position] = start };
        var cameFrom = new Dictionary<GridPosition, GridPosition>();
        var queue = new PriorityQueue<GridPosition, (int Cost, int Wandered)>();
        queue.Enqueue(mover.Position, start);

        while (queue.TryDequeue(out var current, out var reached))
        {
            if (Beats(best.GetValueOrDefault(current, (int.MaxValue, int.MaxValue)), reached))
            {
                continue;
            }

            if (current == destination)
            {
                return new MovementPath(Reconstruct(cameFrom, mover.Position, destination), reached.Cost);
            }

            foreach (var next in current.Neighbours())
            {
                if (!field.IsPassable(next))
                {
                    continue;
                }

                var occupied = occupants.Contains(next);

                if (occupied)
                {
                    // Never end on someone, and pass through only what the printed
                    // clause names: an ally, or anyone with the Incapacitated condition.
                    if (next == destination
                        || !occupants[next].All(other => CanPassThrough(mover, other)))
                    {
                        continue;
                    }
                }

                // "Another creature's space is Difficult Terrain for you unless that
                // creature is Tiny or your ally." Difficult terrain does not stack, so
                // an occupied square that is also rough ground costs double once.
                var stepCost = occupied && !occupants[next].All(other => IsAllyOf(mover, other))
                    ? Math.Max(Battlefield.FeetPerSquare * 2, field.EnterCostFeet(next))
                    : field.EnterCostFeet(next);

                var candidate = (
                    Cost: reached.Cost + stepCost,
                    Wandered: reached.Wandered + AxesOpened(current, next, destination));

                if (candidate.Cost > budgetFeet
                    || !Beats(candidate, best.GetValueOrDefault(next, (int.MaxValue, int.MaxValue))))
                {
                    continue;
                }

                best[next] = candidate;
                cameFrom[next] = current;
                queue.Enqueue(next, candidate);
            }
        }

        return null;
    }

    /// <summary>Whether two creatures are on the same side.</summary>
    private static bool IsAllyOf(Combatant mover, Combatant other) =>
        string.Equals(other.SideId, mover.SideId, StringComparison.Ordinal);

    /// <summary>
    /// Whether the printed rule lets <paramref name="mover"/> walk through
    /// <paramref name="other"/>'s space: "an ally, a creature that has the Incapacitated
    /// condition, a Tiny creature, or a creature that is two sizes larger or smaller
    /// than you".
    /// </summary>
    /// <remarks>
    /// The first two clauses are modelled. The Incapacitated one deliberately ignores
    /// sides — the printed sentence names a condition, not a friend — so a downed enemy
    /// is as passable as a downed ally, and a body can no longer plug a corridor. Tiny
    /// and the two-size clause are not modelled and so still block; both would only make
    /// more squares passable, never fewer, so their absence is the conservative gap.
    /// </remarks>
    private static bool CanPassThrough(Combatant mover, Combatant other) =>
        IsAllyOf(mover, other) || other.HasCondition(ConditionType.Incapacitated);

    /// <summary>
    /// Whether one route to a square is preferable to another: cheaper, or the same
    /// price and less roundabout. Cost always decides first, so the second key can only
    /// choose between routes that cost the same.
    /// </summary>
    private static bool Beats((int Cost, int Wandered) candidate, (int Cost, int Wandered) rival) =>
        candidate.Cost != rival.Cost ? candidate.Cost < rival.Cost : candidate.Wandered < rival.Wandered;

    /// <summary>
    /// How many of the two axes a step moves *away* from the destination on — nought for
    /// a step that closes the gap or holds it, one for a sidestep, two for a step that
    /// retreats on both.
    /// </summary>
    /// <remarks>
    /// This is the whole of the tie-break, and it is deliberately blunt. It says nothing
    /// about the *order* of a route's steps, so going diagonally first and then straight
    /// scores the same as the other way round and either is fine to watch; what it rules
    /// out is the one thing that reads as a mistake, a token walking away from where it
    /// is going and back again because the detour happened to be free.
    /// </remarks>
    private static int AxesOpened(GridPosition from, GridPosition to, GridPosition destination) =>
        (Math.Abs(to.X - destination.X) > Math.Abs(from.X - destination.X) ? 1 : 0)
        + (Math.Abs(to.Y - destination.Y) > Math.Abs(from.Y - destination.Y) ? 1 : 0);

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
