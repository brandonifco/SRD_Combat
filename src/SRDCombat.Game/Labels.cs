using SRDCombat.Core.Combat;

namespace SRDCombat.Game;

/// <summary>
/// One unique letter per combatant, for the grid and for naming targets.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than in the console because every client needs it — a grid has to
/// call its pieces something, and the rule for choosing must not differ between clients.
/// </para>
/// <para>
/// Assigned once at the start of a fight rather than derived from each name, because
/// names collide and the first fight played proved it: an Animated Flying Sword, an Ape
/// and a Cleric called Aldous all drew as <c>A</c>, so the grid was ambiguous and
/// "attack A" would have swung at whichever the search happened to reach first.
/// </para>
/// <para>
/// A combatant keeps a letter from its own name where one is free, so labels stay
/// mnemonic — <c>S</c> is Sable — and only falls back to an arbitrary free letter when
/// its whole name is taken.
/// </para>
/// </remarks>
public sealed class Labels
{
    private readonly Dictionary<string, char> _byId = [];

    private Labels()
    {
    }

    /// <summary>Assigns a label to everyone in the fight, in a fixed order.</summary>
    public static Labels For(IEnumerable<Combatant> combatants)
    {
        var labels = new Labels();
        var taken = new HashSet<char>();

        foreach (var combatant in combatants)
        {
            labels._byId[combatant.Id] = Assign(combatant.Name, taken);
        }

        return labels;
    }

    /// <summary>The letter this combatant is drawn as.</summary>
    public char Of(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return _byId.TryGetValue(combatant.Id, out var label) ? label : '?';
    }

    /// <summary>
    /// Whether a typed token names this combatant: one character is always the label,
    /// anything longer is matched against the name.
    /// </summary>
    /// <remarks>
    /// The split is what makes the labels worth having. Letting a single character also
    /// match a name prefix puts the ambiguity straight back: with an Ape labelled
    /// <c>A</c> and an Aldous labelled <c>L</c>, "a" would match both — the Ape by its
    /// label and Aldous by its name — which is the bug the labels exist to remove.
    /// </remarks>
    public bool Matches(Combatant combatant, string token)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        ArgumentNullException.ThrowIfNull(token);

        return token.Length == 1
            ? char.ToUpperInvariant(token[0]) == Of(combatant)
            : combatant.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase);
    }

    private static char Assign(string name, HashSet<char> taken)
    {
        foreach (var candidate in name.Where(char.IsLetter).Select(char.ToUpperInvariant))
        {
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }

        for (var candidate = 'A'; candidate <= 'Z'; candidate++)
        {
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }

        return '?';
    }
}
