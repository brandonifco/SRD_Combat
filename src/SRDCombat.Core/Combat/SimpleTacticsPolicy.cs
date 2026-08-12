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

        // Escaping comes first and costs the whole action, which is a real choice being
        // made crudely: a grappled creature could instead hit its grappler at no penalty.
        // Getting free is the better default, and without it a grapple would never end,
        // since nothing else in this policy can lift one.
        if (actor.HasCondition(ConditionType.Grappled))
        {
            encounter.Escape();
        }

        if (actor.HasCondition(ConditionType.Prone) && !ConditionRules.IsImmobile(actor))
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

        // "Should I use it now?" — the branch that stops a monster always attacking. If
        // the Attack action reached nothing, a limited-use entry that does reach — the
        // Ape's Rock at 25 feet — is used instead of closing empty-handed.
        if (TryUseLimitedEntry(encounter, actor, target))
        {
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
            // A spent "(Recharge 5-6)" attack would be refused, and the refusal would
            // abort the whole attack loop — filter it out so the next-best attack swings.
            .Where(candidate => actor.Uses.IsAvailable(candidate.Name))
            .OrderByDescending(candidate => candidate.Damage.Sum(damage => damage.Amount.Average))
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return attack is not null && encounter.Attack(attack.Name, target) is null;
    }

    /// <summary>
    /// Uses the hardest-hitting limited-use entry that reaches the target — an attack
    /// like the Ape's Rock, or a saving-throw effect like a breath weapon — when the
    /// Attack action cannot reach anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only entries with a printed usage limit are considered, deliberately. The other
    /// entries locked out of a Multiattack are the lycanthropes' form-gated attacks —
    /// "Bite (Wolf or Hybrid Form Only)" — and the engine has no concept of form, so
    /// choosing one here would be this policy silently deciding what shape the creature
    /// fights in. A client may make that call through <c>UseEntry</c>; this policy does
    /// not.
    /// </para>
    /// <para>
    /// An area entry is skipped when its area would catch the user's own side — the one
    /// piece of judgement this placeholder allows itself, because a wolf breathing on its
    /// own pack reads as a bug in every transcript it appears in. The check still counts
    /// the user among its own side, which now costs nothing: the printed glossary excludes
    /// an Emanation's origin from its area (see <c>AreaTargeting</c>), so no shape this
    /// engine resolves covers its own user, and Emanation entries became choosable when
    /// that reading was verified.
    /// </para>
    /// </remarks>
    private static bool TryUseLimitedEntry(Encounter encounter, Combatant actor, Combatant target)
    {
        if (!actor.Turn.HasAction)
        {
            return false;
        }

        var distance = actor.Position.DistanceFeetTo(target.Position);

        var entry = actor.Stats.Entries
            .Where(candidate => candidate.Section == MonsterEntrySection.Action
                && actor.Uses.Tracks(candidate.Name)
                && actor.Uses.IsAvailable(candidate.Name))
            .Select(candidate => new
            {
                candidate.Name,
                Damage = candidate.Mechanics switch
                {
                    EntryMechanics.Attack => AttackFor(actor, candidate.Name) is { } attack
                        && attack.CanReach(distance)
                            ? attack.Damage.Sum(damage => damage.Amount.Average)
                            : (int?)null,
                    EntryMechanics.SavingThrow => SaveReaches(encounter, actor, target, candidate.Save, distance)
                        ? candidate.Save!.FailureDamage.Sum(damage => damage.Amount.Average)
                        : null,
                    _ => null,
                },
            })
            .Where(candidate => candidate.Damage is not null)
            .OrderByDescending(candidate => candidate.Damage)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return entry is not null && encounter.UseEntry(entry.Name, target) is null;
    }

    private static CombatAttack? AttackFor(Combatant actor, string name) =>
        actor.Stats.Attacks.FirstOrDefault(attack =>
            string.Equals(attack.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a saving-throw entry can be aimed at the target from here without
    /// catching a friend.
    /// </summary>
    private static bool SaveReaches(
        Encounter encounter,
        Combatant actor,
        Combatant target,
        SaveEffect? save,
        int distance)
    {
        if (save is not { DifficultyClass: not null })
        {
            return false;
        }

        if (save.Area is not { } area)
        {
            // A single-target save entry models no range, so nothing gates the distance.
            return true;
        }

        if (!AreaTargeting.CanResolve(area.Shape))
        {
            return false;
        }

        // Cone, Line and Emanation extend from the user; a target beyond their size is
        // out of reach. Point-aimed shapes land wherever they are aimed.
        if (area.Shape is AreaShape.Cone or AreaShape.Line or AreaShape.Emanation
            && area.SizeFeet < distance)
        {
            return false;
        }

        var covered = AreaTargeting.Cover(area, actor.Position, target.Position, encounter.Battlefield)
            .ToHashSet();

        return !encounter.Combatants.Any(combatant =>
            combatant.IsActive
            && combatant.SideId == actor.SideId
            && covered.Contains(combatant.Position));
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
