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
    /// <b>The mover is a space, not a point, and the whole space has to fit.</b> Every
    /// square of the footprint anchored at a candidate square must be on the battlefield
    /// and passable, or the step is not offered — and that is <em>forced by print's
    /// silence</em> rather than chosen. The word "squeeze" appears nowhere in SRD 5.2.1;
    /// the 2014 squeezing rule was not carried into it, and nothing in this document
    /// licenses a creature entering a space its body does not fit. So a gap narrower
    /// than a Large creature's space simply blocks it, and <c>Encounter.Move</c> refuses
    /// with <c>movement.no_room</c> rather than clipping, sliding or squeezing.
    /// </para>
    /// <para>
    /// <b>What a step costs is print's silence too, and this is the stated reading</b>
    /// (#429): a multi-square creature's step costs one square — five feet — per step of
    /// its space rather than per square of its footprint, and the step is Difficult
    /// Terrain if <em>any</em> newly entered square is difficult ground or is another
    /// creature's space under the p. 14 clause. Print writes "Entering a Square" for a
    /// point mover and says nothing about footprints; charging a Huge creature nine
    /// squares per step would make speed meaningless for exactly the creatures the table
    /// gives the most of it to. Difficult Terrain still does not stack: a newly entered
    /// square that is both rough ground and somebody's space costs double once.
    /// </para>
    /// <para>
    /// Occupancy follows the printed <em>Moving around Other Creatures</em> rule: "you
    /// can pass through the space of an ally, a creature that has the Incapacitated
    /// condition, a Tiny creature, or a creature that is two sizes larger or smaller
    /// than you", "another creature's space is Difficult Terrain for you unless that
    /// creature is Tiny or your ally", and "you can't willingly end a move in a space
    /// occupied by another creature". Every clause is asked of each creature whose space
    /// the mover's footprint would <em>overlap</em>, which is what "the space of" means
    /// once a space can be four squares.
    /// </para>
    /// <para>
    /// Two of the four pass-through clauses are modelled: <b>ally</b> and
    /// <b>Incapacitated</b>. The Incapacitated clause names no side, so a downed
    /// <em>enemy</em> is walked through exactly like a downed friend — which is the
    /// point of it. Without that, a body dropped in a doorway walls a side off, and the
    /// tactics policy's stuck-turn rule was carrying the workaround for a gap that
    /// belonged here. <b>Tiny</b> and <b>two size categories apart</b> stay unmodelled
    /// and so still block; both would only widen what is passable, never narrow it.
    /// They land with #429's final slice rather than here, because both read
    /// <c>CombatantStats.Size</c> — which is live content — and turning them on before
    /// spaces are real would change fights in a slice whose whole claim is that it
    /// cannot.
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
    /// <para>
    /// <b>This is the early-exit caller of the one search.</b> The Dijkstra itself lives
    /// in <see cref="Explore"/> and is shared with <see cref="Reachable"/>, so the route
    /// to a square and the answer to "can it be reached at all" cannot drift apart —
    /// they are the same frontier, expanded by the same code and priced by the same
    /// step cost. What stays local to this method is the two destination-dependent
    /// judgements: the <c>CanEndOn</c> pre-check below, and the <see cref="AxesOpened"/>
    /// tie-break, which needs somewhere to be going before it means anything.
    /// <see cref="Reachable"/>'s remarks explain why that keeps a square's *route* a
    /// separate question from its reachability.
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

        if (destination == mover.Position || !SpaceFits(field, mover.SpaceAt(destination)))
        {
            return null;
        }

        var occupants = OccupiedSquares(mover, combatants);

        // A move may end on a downed creature, and nowhere else.
        //
        // This is a HOUSE RULE and the one place this engine knowingly contradicts a
        // printed sentence: "You can't willingly end a move in a space occupied by
        // another creature." Asked for during the 2026-08-16 play session, twice, after
        // the printed reading had been explained — standing over a fallen friend is
        // what a player expects to be able to do, and being unable to finish a move on
        // a body reads as the grid being broken rather than as a rule.
        //
        // Scoped as narrowly as the want allows: only a creature with the Incapacitated
        // condition may be stood on. Everyone else still refuses, so this widens exactly
        // one case and the printed sentence governs every other. It deliberately uses
        // the same predicate as the pass-through clause above, so "can I walk through
        // it" and "can I stop on it" cannot drift apart.
        //
        // The cost is real and is paid in Encounter.ClearSharedSquares: allowing this
        // reopens two creatures in one square, which is the crash that took down two of
        // sixty seeded runs when occupancy was last read as "active". A creature that
        // comes round underneath somebody now displaces them.
        if (Overlapped(occupants, mover.SpaceAt(destination)).Any(other => !CanEndOn(mover, other)))
        {
            return null;
        }

        var search = Explore(field, mover, destination, budgetFeet, occupants);

        return search.DestinationCostFeet is { } cost
            ? new MovementPath(Reconstruct(search.CameFrom, mover.Position, destination), cost)
            : null;
    }

    /// <summary>
    /// Every square this creature could <em>end</em> a move on within a movement budget,
    /// from one bounded search — squares it can only pass through excluded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why it exists (#726, #328).</b> "Where can I go?" was being asked one
    /// destination at a time. The Godot client lit its movement highlight by calling
    /// <see cref="FindPath"/> once per square of the board — 504 searches on the 28 × 18
    /// grid the review quoted, 784 on the board #726's measurement actually landed on,
    /// after every action by every combatant, most of them draining the entire
    /// budget-bounded frontier only to answer "no" — and
    /// <c>SimpleTacticsPolicy.ScoreSquares</c> ran the same full-board loop for its
    /// reposition scan. A Dijkstra from the mover already settles every reachable square
    /// on its way to any one of them; <see cref="FindPath"/> simply threw that away at
    /// its early exit. This keeps it.
    /// </para>
    /// <para>
    /// <b>The set only, never the routes — the decision, and why.</b> One drained
    /// search's predecessor tree is <em>not</em> the set of routes
    /// <see cref="FindPath"/> returns, because <see cref="FindPath"/>'s second key —
    /// <see cref="AxesOpened"/>, "how often does this step move away from where I am
    /// going" — is measured against a destination, and a search with no destination has
    /// nothing to measure it against. Equal-cost routes are plentiful on a grid where
    /// diagonals are free, so the tie-break is not decoration: it decides which of many
    /// twenty-five-foot walks a token is seen to take, and
    /// <c>SimpleTacticsPolicy.ProvokedDamageAlong</c> prices Opportunity Attacks along
    /// exactly that walk. So <b>this method answers reachability and nothing else</b>,
    /// and a caller that needs a route calls <see cref="FindPath"/> for the one
    /// destination it actually chose.
    /// </para>
    /// <para>
    /// The alternative was considered and rejected: make the tie-break
    /// destination-independent (prefer, say, the straightest or earliest-diagonal route)
    /// so one search's predecessors <em>are</em> the routes. It would be faster still
    /// for the policy, and it would change routes — every equal-cost walk currently
    /// chosen by <see cref="AxesOpened"/> is a candidate to move, which moves what
    /// <c>ProvokedDamageAlong</c> charges, which moves which square the policy picks,
    /// which moves the fight. That is a behaviour change wanting its own issue, its own
    /// transcript read and its own pacing verdict, and it buys nothing the two consumers
    /// here were asking for: the client's highlight wants a set, and the policy needs
    /// the same route it prices today. The cost of the split is one extra search per
    /// destination a caller commits to — one per hover for #303's path preview, and
    /// #726 measured that at 0.20 ms (Release) / 0.40 ms (Debug) for a thirty-foot mover
    /// on a 784-square warband board, against the one-per-square-per-action this
    /// replaces. A hover is a frame's worth of budget; a board was not.
    /// </para>
    /// <para>
    /// <b>Why the two agree.</b> <see cref="FindPath"/> answers non-null for exactly the
    /// squares this returns, and the reason is that every destination-dependent
    /// judgement in <see cref="FindPath"/> is either applied here too or provably inert.
    /// The <c>CanEndOn</c> pre-check is applied here, per square. The
    /// <see cref="AxesOpened"/> key can never beat cost (see <see cref="Beats"/>), so it
    /// changes which equal-cost route is kept and never which squares are within budget.
    /// And <see cref="Explore"/>'s stricter <c>CanEndOn</c> test on the step that enters
    /// the destination is inert given that pre-check: the creatures it consults occupy
    /// ground the destination's space covers, so the pre-check has already refused the
    /// square if any of them fails. Argued here; <em>pinned</em> by
    /// <c>MovementRulesTests.Reachable_*</c>, which compares the two answers square by
    /// square over difficult terrain, occupied squares, a Large body and the board edge.
    /// </para>
    /// <para>
    /// The mover's own square is not in the set, matching <see cref="FindPath"/>'s
    /// refusal of a destination equal to the start: "stay where you are" is not a move,
    /// and the client's highlight would otherwise offer a square whose click the engine
    /// refuses.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<GridPosition> Reachable(
        Battlefield field,
        Combatant mover,
        int budgetFeet,
        IReadOnlyCollection<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(combatants);

        var occupants = OccupiedSquares(mover, combatants);
        var search = Explore(field, mover, destination: null, budgetFeet, occupants);
        var reachable = new HashSet<GridPosition>();

        foreach (var square in search.Best.Keys)
        {
            // Every square the search settled other than the start got there through a
            // SpaceFits check, so only the two end-of-move questions are left: the start
            // is not a destination, and a square whose space holds anyone the mover may
            // not stop on is transit-only.
            if (square != mover.Position
                && !Overlapped(occupants, mover.SpaceAt(square)).Any(other => !CanEndOn(mover, other)))
            {
                reachable.Add(square);
            }
        }

        return reachable;
    }

    /// <summary>What one bounded search found: the settled squares, the tree of steps that reached them, and — when the search was aimed at a destination and got there — what that destination cost.</summary>
    /// <param name="Best">Every square settled within budget, with the cost and wandering of the best route to it. Partial when <see cref="DestinationCostFeet"/> is non-null, because an aimed search stops on arrival.</param>
    /// <param name="CameFrom">Each settled square's predecessor on its best route.</param>
    /// <param name="DestinationCostFeet">The destination's cost in feet, or null when the search had no destination or never reached it.</param>
    private sealed record Exploration(
        Dictionary<GridPosition, (int Cost, int Wandered)> Best,
        Dictionary<GridPosition, GridPosition> CameFrom,
        int? DestinationCostFeet);

    /// <summary>
    /// The one movement search, shared by <see cref="FindPath"/> and
    /// <see cref="Reachable"/> so the route to a square and the fact that it is
    /// reachable can never come from two different pieces of code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A uniform-cost search from <c>mover.Position</c>, bounded by
    /// <paramref name="budgetFeet"/>. <paramref name="destination"/> is what separates
    /// the two callers and is the <em>only</em> thing that does: given one, the search
    /// stops the moment that square is settled, carries
    /// <see cref="AxesOpened"/> as its equal-cost tie-break, and applies the stricter
    /// end-of-move test to the step that enters it; given none, it drains the whole
    /// budget and every step is judged by the pass-through rule alone. Both are
    /// Dijkstra over the same weights, so a square's cheapest cost is the same number
    /// either way — see <see cref="Reachable"/>'s remarks for why that makes the two
    /// answers agree.
    /// </para>
    /// <para>
    /// The occupants lookup is a parameter rather than built here because both callers
    /// need it for their own end-of-move checks, and building it twice per query is
    /// waste this search's own history warns about — see the pricing note in the loop.
    /// </para>
    /// </remarks>
    private static Exploration Explore(
        Battlefield field,
        Combatant mover,
        GridPosition? destination,
        int budgetFeet,
        ILookup<GridPosition, Combatant> occupants)
    {
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

            if (destination is { } arrived && current == arrived)
            {
                return new Exploration(best, cameFrom, reached.Cost);
            }

            var currentSpace = mover.SpaceAt(current);

            foreach (var next in current.Neighbours())
            {
                var nextSpace = mover.SpaceAt(next);

                // No squeezing: the whole body has to fit, or the step does not exist.
                if (!SpaceFits(field, nextSpace))
                {
                    continue;
                }

                // Only the ground the body newly covers is entered. A Large creature
                // sliding one square east enters two squares and keeps two, and the two
                // it keeps are neither paid for again nor asked about again.
                var entered = nextSpace.Squares().Where(square => !currentSpace.Contains(square)).ToArray();
                var met = entered.SelectMany(square => occupants[square]).Distinct().ToArray();

                if (met.Length > 0)
                {
                    // Pass through only what the printed clause names — an ally, or
                    // anyone Incapacitated — and stop only on the downed. The
                    // destination arm is FindPath's alone: a destinationless search is
                    // asking which squares are *walkable*, and Reachable applies the
                    // end-of-move test itself, to every square, afterwards.
                    var blocked = destination is { } aim && next == aim
                        ? met.Any(other => !CanEndOn(mover, other))
                        : met.Any(other => !CanPassThrough(mover, other));

                    if (blocked)
                    {
                        continue;
                    }
                }

                // `entered`/`met` are already in hand from the blocked-check just above —
                // the private overload prices them directly rather than the public
                // StepCostFeet rebuilding an occupants lookup per neighbour. This
                // Dijkstra can visit this line thousands of times in one search (every
                // legal neighbour of every node it explores), so a per-call lookup rebuild
                // here is not a rounding error: it turned one full-board reposition scan
                // (SimpleTacticsPolicy.ScoreSquares, one FindPath per battlefield square)
                // from seconds into tens of minutes. Measured, not assumed — see the PR.
                var stepCost = StepCostFeet(field, mover, entered, met);

                var candidate = (
                    Cost: reached.Cost + stepCost,
                    // No destination, nothing to wander away from: the key is held at
                    // nought so Beats reduces to plain cost, which is what a
                    // reachability question wants and all it can honestly answer.
                    Wandered: reached.Wandered
                        + (destination is { } goal ? AxesOpened(current, next, goal) : 0));

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

        return new Exploration(best, cameFrom, null);
    }

    /// <summary>
    /// Who stands where, for a given mover: every square of every other creature's
    /// space, keyed to the creatures standing in it.
    /// </summary>
    /// <remarks>
    /// Anyone still on the field occupies their square, conscious or not. Reading it as
    /// "active" let a creature end its move on a downed one — harmless until healing
    /// arrived, at which point the downed creature stood up inside somebody else and the
    /// next path finder found two combatants in one square.
    /// <para>
    /// Keyed as a lookup rather than a dictionary for the same reason: two creatures
    /// sharing a square is a state the movement rules must survive rather than throw on,
    /// whatever produced it. Every occupant of a square is consulted, so a square holding
    /// both a friend and a stranger is judged by the stranger.
    /// </para>
    /// <para>
    /// The public <see cref="StepCostFeet(Battlefield, Combatant, GridPosition,
    /// GridPosition, IReadOnlyCollection{Combatant})"/> overload deliberately builds this
    /// lookup itself rather than calling here, and is left doing so on purpose:
    /// <c>MovementRulesTests.StepCostFeet_SumsToPathCostFeet_ThroughAGenuinelyDead
    /// CreaturesSquare</c> asserts the concrete cost (10) as well as the sum, so it would
    /// still catch a shared lookup that wrongly counted the dead — the duplication is kept
    /// so the two readings of "who occupies this square" stay independently checkable,
    /// not because the test depends on it.
    /// </para>
    /// </remarks>
    private static ILookup<GridPosition, Combatant> OccupiedSquares(
        Combatant mover,
        IReadOnlyCollection<Combatant> combatants) =>
        combatants
            .Where(other => other.Id != mover.Id && !other.IsDead)
            .SelectMany(other => other.Space.Squares(), (other, square) => (Square: square, Creature: other))
            .ToLookup(entry => entry.Square, entry => entry.Creature);

    /// <summary>
    /// The movement cost in feet of one step of a walk: entering <paramref name="step"/> from
    /// <paramref name="from"/>, double when the newly-covered ground meets a non-ally's space
    /// or any of it is Difficult Terrain, otherwise the plain per-square cost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one cost authority both <see cref="FindPath"/> and <c>Encounter.WalkPath</c> price a
    /// step with, so a route's <see cref="MovementPath.CostFeet"/> and a partial walk's spend
    /// can never disagree (pinned by a trip-wire test summing this over a full path against
    /// <see cref="MovementPath.CostFeet"/>).
    /// </para>
    /// <para>
    /// <paramref name="from"/> is load-bearing, not a convenience. The price depends on which
    /// ground is <em>newly</em> covered — "only the ground the body newly covers is entered",
    /// per <see cref="FindPath"/>'s remarks — which depends on where the mover is leaving from.
    /// Inside <see cref="FindPath"/>'s search that is the Dijkstra node being expanded, never
    /// <c>mover.Position</c>: the search never calls <c>MoveTo</c>, so <c>mover.Position</c>
    /// stays pinned at the true start of the whole search while the node being expanded walks
    /// the graph. Deriving "from" as <c>mover.Position</c> would re-price every step past the
    /// first for a multi-square mover — re-treating ground the body already covers as newly
    /// entered, and re-charging a Difficult Terrain square it is already standing on.
    /// </para>
    /// </remarks>
    public static int StepCostFeet(
        Battlefield field,
        Combatant mover,
        GridPosition from,
        GridPosition step,
        IReadOnlyCollection<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(combatants);

        var currentSpace = mover.SpaceAt(from);
        var nextSpace = mover.SpaceAt(step);

        // Only the ground the body newly covers is entered — see FindPath's remarks.
        var entered = nextSpace.Squares().Where(square => !currentSpace.Contains(square)).ToArray();

        var occupants = combatants
            .Where(other => other.Id != mover.Id && !other.IsDead)
            .SelectMany(other => other.Space.Squares(), (other, square) => (Square: square, Creature: other))
            .ToLookup(entry => entry.Square, entry => entry.Creature);

        var met = entered.SelectMany(square => occupants[square]).Distinct().ToArray();

        return StepCostFeet(field, mover, entered, met);
    }

    /// <summary>
    /// The shared pricing core <see cref="StepCostFeet(Battlefield, Combatant, GridPosition,
    /// GridPosition, IReadOnlyCollection{Combatant})"/> and <see cref="FindPath"/>'s inner
    /// loop both price a step with, given the ground newly covered and who occupies it.
    /// </summary>
    /// <remarks>
    /// Split out so <see cref="FindPath"/> can call it with the <c>entered</c>/<c>met</c> it
    /// has already computed for the blocked-square check just above, rather than the public
    /// overload's rebuilding an occupants lookup from scratch. That rebuild is cheap once —
    /// it is what <see cref="Encounter.WalkPath"/> pays, at most one per square actually
    /// walked — but <c>FindPath</c>'s Dijkstra search calls this on every legal neighbour of
    /// every node it explores, which can run to thousands of calls in one search. Measured:
    /// rebuilding the lookup that many times turned <c>SimpleTacticsPolicy.ScoreSquares</c>'s
    /// one-<c>FindPath</c>-per-battlefield-square scan from seconds into tens of minutes.
    /// "Another creature's space is Difficult Terrain for you unless that creature is Tiny or
    /// your ally." Difficult terrain does not stack, so a newly entered square that is also
    /// rough ground costs double once — and one difficult square anywhere in the newly
    /// entered ground makes the whole step difficult.
    /// </remarks>
    private static int StepCostFeet(
        Battlefield field,
        Combatant mover,
        IReadOnlyCollection<GridPosition> entered,
        IReadOnlyCollection<Combatant> met) =>
        met.Any(other => !IsAllyOf(mover, other))
            || entered.Any(square => field.EnterCostFeet(square) > Battlefield.FeetPerSquare)
            ? Battlefield.FeetPerSquare * 2
            : Battlefield.FeetPerSquare;

    /// <summary>
    /// Whether a creature's whole body fits here: every square of the space on the
    /// battlefield and passable.
    /// </summary>
    /// <remarks>
    /// <b>There is no squeezing in SRD 5.2.1.</b> The word appears nowhere in the
    /// document — the 2014 rule was not carried into it — so nothing licenses a creature
    /// entering a space its body does not fit, and a gap narrower than a Large creature's
    /// space simply blocks it. Forced by print's silence rather than chosen, and stated
    /// here because a reader would otherwise reasonably expect the older rule.
    /// </remarks>
    public static bool SpaceFits(Battlefield field, CreatureSpace space)
    {
        ArgumentNullException.ThrowIfNull(field);

        return space.Squares().All(field.IsPassable);
    }

    /// <summary>
    /// The distinct creatures whose spaces this space overlaps, in the order the lookup
    /// hands them back — deterministic, because a seed replays.
    /// </summary>
    private static IEnumerable<Combatant> Overlapped(
        ILookup<GridPosition, Combatant> occupants,
        CreatureSpace space) =>
        space.Squares().SelectMany(square => occupants[square]).Distinct();

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
    /// Whether a move may <em>finish</em> in a square this creature is standing in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only over a <em>fallen ally</em>, and this is the engine's one deliberate
    /// contradiction of a printed sentence — see the note in <see cref="FindPath"/>.
    /// </para>
    /// <para>
    /// Both halves of the condition are the scope the request actually had: "I want to
    /// be able to walk into a space where a fallen comrade lays." A downed *enemy* is
    /// left alone, so standing over a body to defend it works and standing on a corpse
    /// you made does not. That narrowness is worth keeping for a second reason — the
    /// stuck-turn last resort in <c>SimpleTacticsPolicy</c> exists because a creature
    /// with nowhere to go must still be able to act, and letting a monster *stop* on
    /// the body it is trying to get past would quietly delete the only scenario that
    /// rule is tested against.
    /// </para>
    /// </remarks>
    private static bool CanEndOn(Combatant mover, Combatant other) =>
        IsAllyOf(mover, other) && other.HasCondition(ConditionType.Incapacitated);

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
    /// <para>
    /// The trigger is precise: the creature must <em>leave</em> the enemy's reach, so an
    /// enemy who was already out of reach, or who is still in reach after the step, gets
    /// nothing. Taking the Disengage action avoids provoking entirely, and an enemy who
    /// cannot act or has already spent its Reaction cannot make one.
    /// </para>
    /// <para>
    /// Reach is measured <b>space to space</b>, between the nearest squares of the
    /// threatening creature's space and the mover's — printed page 13 counts range "from
    /// a square adjacent to one of them" and stops "in the space of the other one". So a
    /// Large creature with five feet of reach threatens the whole ring around its 2 by 2
    /// space, not the ring around one corner of it, and a Large <em>mover</em> provokes
    /// while any square of its body is still inside the threatened ring. Both spaces are
    /// taken at the step being judged rather than from <c>Position</c>, because this is
    /// asked one square at a time as the walk happens.
    /// </para>
    /// <para>
    /// "A creature that you can see leaves your reach" (#672) additionally requires the
    /// enemy to have line of sight to the mover's space at <paramref name="from"/> — the
    /// square being left, the same instant the trigger itself is printed against. A
    /// Blinded enemy therefore makes no Opportunity Attacks, closing a gap #671 flagged:
    /// cover already refused one through a wall (<c>Encounter.MakeOpportunityAttack</c>),
    /// but nothing before this stopped a Blinded reactor swinging on a mover it could not
    /// see. Since #673, "that you can see" also composes with the mover being Invisible
    /// (Hide's hidden Rogue provokes no Opportunity Attack by walking away) — the same
    /// Blindsight/Truesight-defeat clause every other sight-gated reading now shares.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Combatant> FindOpportunityAttackers(
        Battlefield field,
        Combatant mover,
        GridPosition from,
        GridPosition to,
        IReadOnlyCollection<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(field);
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
            .Where(enemy => VisionRules.CanSee(field, enemy, mover, mover.SpaceAt(from)))
            .Where(enemy =>
            {
                var reach = MeleeReachFeet(enemy);
                var wasInReach = enemy.Space.DistanceFeetTo(mover.SpaceAt(from)) <= reach;
                var stillInReach = enemy.Space.DistanceFeetTo(mover.SpaceAt(to)) <= reach;

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
