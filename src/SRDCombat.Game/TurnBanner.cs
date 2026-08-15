using SRDCombat.Core.Combat;

namespace SRDCombat.Game;

/// <summary>
/// The answer to "who is acting, and with what?" — the name, the class and level when
/// the actor is a character, armor class and hit points on one line, and the attacks
/// they fight with, each with its damage expression, on the next.
/// </summary>
/// <remarks>
/// Computed here rather than in a client for the same reason <see cref="OfferEffect"/>
/// is: the banner is a reading of engine state, and two clients composing it separately
/// would be two places for it to drift. Nothing here is recomputed — every number is
/// read straight off the combatant.
/// </remarks>
public static class TurnBanner
{
    /// <summary>
    /// One or two lines: "Brenna — Fighter 5 — AC 18 — 17/28 hp", then
    /// "Longsword 1d8 + 3 Slashing · Shortbow 1d6 + 3 Piercing" when the actor has
    /// attacks at all. Two lines rather than one because the second is the one that
    /// grows, and a client fitting them to a screen should never have to cut the first.
    /// </summary>
    public static IReadOnlyList<string> Lines(Combatant actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var identity = new List<string> { actor.Name };

        if (actor.Stats.Character is { ClassName: { } className } character)
        {
            identity.Add($"{className} {character.Level}");
        }

        identity.Add($"AC {actor.Stats.ArmorClass}");
        identity.Add($"{actor.CurrentHitPoints}/{actor.Stats.MaximumHitPoints} hp");

        var lines = new List<string> { string.Join(" — ", identity) };

        if (actor.Stats.Attacks.Count > 0)
        {
            lines.Add(string.Join(" · ", actor.Stats.Attacks.Select(Describe)));
        }

        return lines;
    }

    /// <summary>
    /// "Longsword 1d8 + 3 Slashing" — several components joined with "plus" the way the
    /// stat blocks print them, and a conditional component saying when it applies, so
    /// the goblin's "if the attack roll had Advantage" die is never shown as certain.
    /// </summary>
    private static string Describe(CombatAttack attack)
    {
        var always = string.Join(" plus ", attack.Damage
            .Where(component => component.Condition is null)
            .Select(component => $"{component.Amount} {component.Type}"));

        var conditional = string.Concat(attack.Damage
            .Where(component => component.Condition is not null)
            .Select(component => $" (+{component.Amount} {component.Type} with Advantage)"));

        return $"{attack.Name} {always}{conditional}".TrimEnd();
    }
}
