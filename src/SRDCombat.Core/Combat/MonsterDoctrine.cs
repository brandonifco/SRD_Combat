using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// The monsters' half of the Phase 6 split (#127): what a creature hunts, decided by
/// what its stat block says it is. Pack hunters flank whatever their trait pays for,
/// creatures with a humanlike mind converge the way the party does, and everything
/// else stays greedy-simple — a Boar should feel dumber than a squad.
/// </summary>
/// <remarks>
/// <para>
/// The gate is the stat block's own Intelligence score, and the threshold is a stated
/// reading: <see cref="TacticalIntelligence"/> is 8, the bottom of the humanlike range
/// — a Goblin Warrior (10) coordinates, an Ogre (5) charges, a Wolf (3) knows only
/// what its pack instinct tells it. Like <see cref="PartyDoctrine"/>, this class holds
/// no state: every answer is a pure function of the encounter, because the frozen
/// transcripts rest on determinism.
/// </para>
/// <para>
/// Deliberately <em>not</em> the party's whole doctrine. Monsters get no engagement
/// phase, no healer awareness, no area patience — those are squad judgements, and a
/// drawn encounter is a hunting pack or a warband, not a squad. What a smart monster
/// gets is the one behavior that measured largest for the party: convergence on the
/// kill that buys the most safety soonest.
/// </para>
/// </remarks>
public static class MonsterDoctrine
{
    /// <summary>
    /// The Intelligence at which a monster fights tactically rather than greedily:
    /// 8, the bottom of the humanlike range.
    /// </summary>
    public const int TacticalIntelligence = 8;

    /// <summary>
    /// The enemy this monster should be fighting. A pack hunter takes the enemy an
    /// able packmate already stands beside — the condition its own trait pays
    /// Advantage for — a tactical mind converges on the shared kill exactly as the
    /// party does, and everything else keeps the greedy nearest-weakest choice the
    /// caller passes in.
    /// </summary>
    /// <remarks>
    /// The pack rule outranks the Intelligence gate on purpose: a Wolf is INT 3 and
    /// flanks anyway, because the flank is instinct rather than tactics — and for a
    /// pack, flanking <em>is</em> focus fire, since the second wolf piling onto the
    /// first one's prey is what makes both of their jaws find the mark.
    /// </remarks>
    public static Combatant? ChooseTarget(Encounter encounter, Combatant actor, Combatant? nearest)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        // The other half of the split lives in PartyDoctrine; a character asking here
        // is a caller's mistake answered gently.
        if (actor.Stats.Character is not null)
        {
            return nearest;
        }

        if (actor.HasTrait(MonsterTrait.PackTactics)
            && PackTarget(encounter, actor) is { } prey)
        {
            return prey;
        }

        var intelligence = actor.Stats.Abilities.TryGetValue(Ability.Intelligence, out var ability)
            ? ability.Score
            : 0;

        return intelligence >= TacticalIntelligence
            ? PartyDoctrine.Converge(encounter, actor, nearest)
            : nearest;
    }

    /// <summary>
    /// The enemy whose flank is already turned: an able packmate stands within 5 feet
    /// of it, which is the exact condition the engine's Pack Tactics check pays
    /// Advantage for. Preferred in reach first — a turn in reach of paying prey is
    /// never spent walking — then anywhere on the field, so a wolf out of position
    /// closes on the fight its pack has already picked. Null when no enemy is
    /// flanked, which sends the asker back to its greedy instinct.
    /// </summary>
    private static Combatant? PackTarget(Encounter encounter, Combatant actor)
    {
        var flanked = encounter.EnemiesOf(actor)
            .Where(enemy => encounter.Combatants.Any(ally => ally.SideId == actor.SideId
                && ally.Id != actor.Id
                && ally.IsActive
                && ally.DistanceFeetTo(enemy) <= Battlefield.FeetPerSquare))
            .ToArray();

        if (flanked.Length == 0)
        {
            return null;
        }

        var inReach = flanked
            .Where(enemy => actor.Stats.Attacks.Any(attack =>
                attack.CanReach(actor.DistanceFeetTo(enemy)))
                && CoverRules.AgainstSpace(encounter.Battlefield, actor.Space, enemy.Space, encounter.Combatants)
                    != CoverDegree.Total)
            .ToArray();

        return (inReach.Length > 0 ? inReach : flanked)
            .OrderBy(enemy => actor.DistanceFeetTo(enemy))
            .ThenBy(enemy => enemy.CurrentHitPoints)
            .ThenBy(enemy => enemy.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
