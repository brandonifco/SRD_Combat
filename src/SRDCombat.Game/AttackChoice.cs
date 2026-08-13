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
    /// <summary>The hardest-hitting of the attacker's attacks that reaches the target, or null.</summary>
    public static CombatAttack? BestFor(Combatant attacker, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        var distance = attacker.Position.DistanceFeetTo(target.Position);

        return attacker.Stats.Attacks
            .Where(attack => attack.CanReach(distance))
            .OrderByDescending(attack => attack.Damage.Sum(damage => damage.Amount.Average))
            .ThenBy(attack => attack.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
