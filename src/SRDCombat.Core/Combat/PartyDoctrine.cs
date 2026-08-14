using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// What a character is for, derived from what it carries — never from its class name,
/// so a created party with an odd composition still gets sensible doctrine.
/// </summary>
public enum PartyRole
{
    /// <summary>Melee-primary: stands where the enemy must walk.</summary>
    FrontLine,

    /// <summary>Ranged-primary: shoots from standoff.</summary>
    Skirmisher,

    /// <summary>Carries healing or revival: keeps the others alive, fights second.</summary>
    Support,

    /// <summary>Carries area damage: never ahead of the screen.</summary>
    Artillery,
}

/// <summary>
/// The side's posture this round: fire from standoff, or close and fight.
/// </summary>
/// <remarks>
/// Squad AI slice 4 (#125), shipped as arithmetic the movement policy does not yet
/// consult — the same verdict as #124's screening, reached the same way. Every wiring
/// of Hold into <c>SimpleTacticsPolicy</c> was measured over the recorded seeds and
/// none paid: front liners standing the screen behind any entry condition halved the
/// median or worse, and the forms that preserved the median were the ones that never
/// fired. The structural reason is on <see cref="PartyDoctrine.Phase"/>.
/// </remarks>
public enum EngagementPhase
{
    /// <summary>
    /// The party out-ranges the enemy and nothing threatens anyone yet: ranged
    /// members fire from where they stand, front liners stand the screen line.
    /// </summary>
    Hold,

    /// <summary>Close and fight — the default whenever holding does not pay.</summary>
    Commit,
}

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
    /// The side's posture: <see cref="EngagementPhase.Hold"/> only while holding pays
    /// twice over — the party's expected ranged damage per round exceeds the enemies'
    /// by more than the front-line output holding idles, and no enemy is within one
    /// move and a reach of contact with anyone. Everything else is
    /// <see cref="EngagementPhase.Commit"/>, including every monster's turn. A
    /// melee-heavy party commits early by arithmetic rather than by a special rule:
    /// its ranged margin can never cover what its front line is not swinging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not yet consulted by movement, and the reason is measured.</b> Six wirings
    /// were built and dismantled over the recorded seeds 1–40 against main's 8: hold
    /// on any positive ranged margin with front liners standing the lane, median 4;
    /// requiring the enemy to have no ranged answer at all, 6; the screen placed
    /// forward as an interceptor, 3; and this final arithmetic — which never fires
    /// for the pregenerated party, correctly reading two heavy melee as too expensive
    /// to idle — left the median at exactly 8 while a ranged-heavy created party it
    /// does fire for measured 3 against its own baseline 3. Holding never paid in any
    /// form, only ever cost.
    /// </para>
    /// <para>
    /// The structural reason, worth keeping for whoever revisits this: the sides
    /// start one move apart, so there are no standoff rounds for a phase to spend —
    /// an enemy that cannot be held off for even one free round makes every hold a
    /// donation of the front line's output. A battlefield with longer engagement
    /// distances, or a monster doctrine that itself holds (#127), is what would give
    /// this phase a theatre; the arithmetic is ready for the day one exists.
    /// </para>
    /// </remarks>
    public static EngagementPhase Phase(Encounter encounter, Combatant actor)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.Stats.Character is null)
        {
            return EngagementPhase.Commit;
        }

        var enemies = encounter.EnemiesOf(actor).Where(enemy => !enemy.IsDead).ToArray();

        if (enemies.Length == 0)
        {
            return EngagementPhase.Commit;
        }

        var allies = encounter.Combatants
            .Where(ally => ally.SideId == actor.SideId
                && ally.IsActive
                && ally.Stats.Character is not null)
            .ToArray();

        var partyRanged = allies.Sum(RangedThreatPerRound);
        var enemyRanged = enemies.Sum(RangedThreatPerRound);

        // The exchange, per round of standoff: holding earns the ranged margin and
        // idles the front line's whole output. A melee-heavy party commits early
        // because its margin can never cover what its front line is not swinging.
        var idledFrontLine = allies
            .Where(ally => RoleOf(ally) == PartyRole.FrontLine)
            .Sum(ThreatPerRound);

        if (partyRanged - enemyRanged <= idledFrontLine)
        {
            return EngagementPhase.Commit;
        }

        var backs = allies.Where(ally => RoleOf(ally) != PartyRole.FrontLine).ToArray();

        if (backs.Length == 0)
        {
            return EngagementPhase.Commit;
        }

        var breached = enemies.Any(enemy => allies.Any(ally =>
            enemy.Position.DistanceFeetTo(ally.Position)
                <= enemy.Stats.SpeedFeet + MovementRules.MeleeReachFeet(enemy)));

        return breached ? EngagementPhase.Commit : EngagementPhase.Hold;
    }

    /// <summary>
    /// Roughly how much damage this creature deals per round from standoff: its best
    /// available ranged attack's average times the swings its action buys, or its best
    /// at-will damaging cantrip when that is bigger. Zero for a melee-only creature,
    /// which is what makes the phase arithmetic read a melee-heavy side as one that
    /// should not be holding.
    /// </summary>
    /// <remarks>
    /// The same altitude as <see cref="ThreatPerRound"/>: an estimate for comparing
    /// against numbers made the same way, not a simulation. Slotted spells are not
    /// counted — a standoff lasts rounds and slots do not — and neither are limited-use
    /// entries, so a creature whose thrown Rock is spent counts as the melee creature
    /// it now is.
    /// </remarks>
    public static double RangedThreatPerRound(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        var weapon = combatant.Stats.Attacks
            .Where(attack => attack.NormalRangeFeet is not null)
            .Where(attack => combatant.Uses.IsAvailable(attack.Name))
            .Select(attack => attack.Damage.Sum(damage => damage.Amount.Average))
            .DefaultIfEmpty(0)
            .Max();

        var swings = combatant.Stats.Multiattack?.AttackCount
            ?? combatant.Stats.Character?.AttacksPerAction
            ?? 1;

        var cantrip = combatant.Stats.Character?.Spells
            .Where(spell => spell.IsCantrip)
            .Where(spell => (spell.TargetRangeFeet ?? 0) > Battlefield.FeetPerSquare)
            .Select(spell => spell.Damage.Sum(damage => damage.Amount.Average)
                + (spell.Save?.FailureDamage.Sum(damage => damage.Amount.Average) ?? 0))
            .DefaultIfEmpty(0)
            .Max() ?? 0;

        return Math.Max(weapon * swings, cantrip);
    }

    /// <summary>
    /// The role this character's own kit assigns it, by a stated precedence: healing
    /// outranks everything (a party's one healer is its Support whatever else it
    /// carries), then area damage, then a ranged primary weapon, then the front line.
    /// A monster has no role; the caller's <c>Stats.Character</c> gate keeps it from
    /// asking.
    /// </summary>
    public static PartyRole RoleOf(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        if (combatant.Stats.Character is not { } character)
        {
            return PartyRole.FrontLine;
        }

        if (character.Spells.Any(spell => spell.Heal is not null || spell.Revival is not null))
        {
            return PartyRole.Support;
        }

        if (character.Spells.Any(spell => spell.Save?.Area is not null))
        {
            return PartyRole.Artillery;
        }

        var longestReach = combatant.Stats.Attacks
            .Select(attack => attack.MaximumRangeFeet)
            .DefaultIfEmpty(0)
            .Max();

        return longestReach > Battlefield.FeetPerSquare ? PartyRole.Skirmisher : PartyRole.FrontLine;
    }

    /// <summary>
    /// The squares the enemies would walk to reach this side's back rank: each living
    /// enemy's cheapest path to its nearest non-front-line member, unioned. A front
    /// liner standing on one of these is a screen — and a gap between generated walls
    /// becomes a held doorway without any code knowing what a doorway is.
    /// </summary>
    /// <remarks>
    /// The paths are computed as if the asking front liner were not on the board —
    /// otherwise its own body diverts the lane it is trying to stand in — and with the
    /// destination member lifted too, since a path may not end on an occupied square.
    /// An enemy's path is where it would go <em>today</em>: the lanes shift as the
    /// fight moves, and the front liner shifts with them.
    /// </remarks>
    public static IReadOnlySet<GridPosition> EnemyLanes(Encounter encounter, Combatant actor)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        var backs = encounter.Combatants
            .Where(ally => ally.SideId == actor.SideId
                && ally.Id != actor.Id
                && ally.IsActive
                && ally.Stats.Character is not null
                && RoleOf(ally) != PartyRole.FrontLine)
            .ToArray();

        var lanes = new HashSet<GridPosition>();

        if (backs.Length == 0)
        {
            return lanes;
        }

        foreach (var enemy in encounter.EnemiesOf(actor).Where(enemy => !enemy.IsDead))
        {
            var back = backs
                .OrderBy(candidate => enemy.Position.DistanceFeetTo(candidate.Position))
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .First();

            var path = MovementRules.FindPath(
                encounter.Battlefield,
                enemy,
                back.Position,
                budgetFeet: 1000,
                encounter.Combatants
                    .Where(other => other.Id != actor.Id && other.Id != back.Id)
                    .ToArray());

            foreach (var step in path?.Steps ?? [])
            {
                lanes.Add(step);
            }
        }

        return lanes;
    }

    /// <summary>
    /// The closest any living front liner on this side currently stands to an enemy —
    /// the screen's depth. Null when the side has no front line, in which case nobody
    /// can be behind it.
    /// </summary>
    public static int? ScreenDistanceFeet(Encounter encounter, Combatant actor)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        var enemies = encounter.EnemiesOf(actor).Where(enemy => !enemy.IsDead).ToArray();

        if (enemies.Length == 0)
        {
            return null;
        }

        var distances = encounter.Combatants
            .Where(ally => ally.SideId == actor.SideId
                && ally.Id != actor.Id
                && ally.IsActive
                && ally.Stats.Character is not null
                && RoleOf(ally) == PartyRole.FrontLine)
            .Select(ally => enemies.Min(enemy => ally.Position.DistanceFeetTo(enemy.Position)))
            .ToArray();

        return distances.Length > 0 ? distances.Min() : null;
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
