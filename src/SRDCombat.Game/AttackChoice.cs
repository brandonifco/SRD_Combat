using SRDCombat.Core.Combat;

namespace SRDCombat.Game;

/// <summary>
/// The attack a client offers by default when the player names none.
/// </summary>
/// <remarks>
/// This is a client convenience, not a rule: the engine takes whatever attack name it is
/// given and refuses the ones that cannot land. The default is the hardest-hitting attack
/// that reaches, which is the choice a player would make by hand every time — and it
/// lives here so the console and the Godot client cannot drift apart on it.
/// </remarks>
public static class AttackChoice
{
    /// <summary>
    /// The best of the attacker's attacks against this target: the hardest-hitting one
    /// that reaches, except that a bow is never chosen with an enemy in your face.
    /// </summary>
    /// <param name="attacker">Who is swinging.</param>
    /// <param name="target">Who they are swinging at.</param>
    /// <param name="combatants">
    /// The fight's roster, so "an enemy within 5 feet" can be read the way the printed
    /// rule states it — <em>any</em> able enemy, not just the target. Null falls back to
    /// the target's own distance, which is the common case and the one a client without
    /// a roster to hand can still get right.
    /// </param>
    /// <remarks>
    /// <b>Reaching is not the same as being worth using.</b> "If you make a ranged attack
    /// roll while within 5 feet of an enemy who can see you … you have Disadvantage"
    /// (printed page 15), and a Shortbow reaches an adjacent target perfectly well — so
    /// with a Rogue carrying a Shortsword and a Shortbow, both averaging the same, the
    /// old ordering broke the tie alphabetically and fired the <em>bow</em> point blank,
    /// at Disadvantage, with a blade on the character's belt. A penalised attack now
    /// sorts below every unpenalised one, and only then does damage decide.
    /// </remarks>
    public static CombatAttack? BestFor(
        Combatant attacker,
        Combatant target,
        IReadOnlyList<Combatant>? combatants = null)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        var distance = attacker.DistanceFeetTo(target);

        var crowded = combatants is null
            ? distance <= Battlefield.FeetPerSquare
            : combatants.Any(other => other.SideId != attacker.SideId
                && other.IsActive
                && attacker.DistanceFeetTo(other) <= Battlefield.FeetPerSquare);

        return attacker.Stats.Attacks
            .Where(attack => attack.CanReach(distance))
            .OrderBy(attack => crowded && attack.IsRangedAttackRoll(distance) ? 1 : 0)
            .ThenByDescending(attack => attack.Damage.Sum(damage => damage.Amount.Average))
            .ThenBy(attack => attack.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
