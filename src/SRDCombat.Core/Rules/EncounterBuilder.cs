using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Rules;

/// <summary>An encounter's monsters, and what it cost against its budget.</summary>
/// <param name="Monsters">The creatures, in the order they were chosen.</param>
/// <param name="Budget">The XP budget the encounter was built to.</param>
/// <param name="Spent">The printed XP actually spent. Never more than the budget.</param>
public sealed record BuiltEncounter(IReadOnlyList<MonsterDefinition> Monsters, int Budget, int Spent)
{
    /// <summary>Budget left unspent. "It's OK if you have a few unspent XP left over."</summary>
    public int Remaining => Budget - Spent;
}

/// <summary>
/// Step 3 of the SRD's encounter guidelines: spend an XP budget on monsters.
/// </summary>
/// <remarks>
/// <para>
/// "Every creature has an XP value in its stat block. When you add a creature to your
/// combat encounter, deduct its XP from your XP budget ... Spend as much of your XP
/// budget as you can without going over. It's OK if you have a few unspent XP left over."
/// </para>
/// <para>
/// <b>The XP spent is the creature's <em>printed</em> value</b>, not one derived from its
/// Challenge Rating, because that is what the sentence says. The two disagree exactly
/// once in this bestiary — the Archmage prints 8,000 XP where its CR 12 is worth 8,400,
/// a real inconsistency the extractor already warns about — and following the printed
/// number keeps the builder honest to the page.
/// </para>
/// <para>
/// <b>What the builder decides and what it does not.</b> It picks from whatever pool it
/// is handed; deciding which monsters are fit to use is <see cref="MonsterPool"/>'s job,
/// and the two are kept apart deliberately — coverage is not difficulty. It spends the
/// budget the caller computed; deciding the budget is <see cref="EncounterBudget"/>'s.
/// </para>
/// <para>
/// <b>Two stated interpretations, because the page does not settle them.</b> The SRD's
/// own examples run from one Bugbear Warrior to nine Stirges, so a count limit is not in
/// the book — but a grid fight and a turn loop both have opinions, so
/// <paramref name="maximumMonsters"/> caps it and defaults to eight. And the book gives
/// no selection procedure at all, so this picks uniformly at random among everything
/// still affordable, which produces the mix of shapes the examples show rather than
/// always buying the biggest creature that fits.
/// </para>
/// </remarks>
public static class EncounterBuilder
{
    /// <summary>How many monsters an encounter may contain unless told otherwise.</summary>
    public const int DefaultMaximumMonsters = 8;

    /// <summary>Builds an encounter to a budget from a pool of candidates.</summary>
    /// <param name="candidates">The monsters that may be used. Usually a <see cref="MonsterPool"/> draw.</param>
    /// <param name="budget">The XP to spend, from <see cref="EncounterBudget.For"/>.</param>
    /// <param name="random">The dice. Determinism is what makes a fight reproducible from its seed.</param>
    /// <param name="maximumMonsters">The most creatures to field.</param>
    public static BuiltEncounter Build(
        IEnumerable<MonsterDefinition> candidates,
        int budget,
        IRandomSource random,
        int maximumMonsters = DefaultMaximumMonsters)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMonsters, 1);

        // Ordered before anything is chosen: the caller's sequence might be a LINQ query
        // whose order is incidental, and a reproducible fight cannot rest on that.
        var affordable = candidates
            .Where(monster => monster.ExperiencePoints > 0)
            .OrderBy(monster => monster.ExperiencePoints)
            .ThenBy(monster => monster.Id, StringComparer.Ordinal)
            .ToArray();

        var chosen = new List<MonsterDefinition>();
        var remaining = budget;

        while (chosen.Count < maximumMonsters)
        {
            var options = affordable
                .Where(monster => monster.ExperiencePoints <= remaining)
                .ToArray();

            if (options.Length == 0)
            {
                break;
            }

            // Roll(n) returns 1..n, so this indexes the options uniformly.
            var pick = options[random.Roll(options.Length) - 1];

            chosen.Add(pick);
            remaining -= pick.ExperiencePoints;
        }

        return new BuiltEncounter(chosen, budget, budget - remaining);
    }

    /// <summary>
    /// Builds an encounter for a party, doing all three of the SRD's steps.
    /// </summary>
    /// <remarks>
    /// The convenience shape the gauntlet and the client both want: choose a difficulty,
    /// get the budget, spend it.
    /// </remarks>
    public static BuiltEncounter ForParty(
        IEnumerable<MonsterDefinition> candidates,
        int partySize,
        int partyLevel,
        EncounterDifficulty difficulty,
        IRandomSource random,
        int maximumMonsters = DefaultMaximumMonsters) =>
        Build(
            candidates,
            EncounterBudget.For(partySize, partyLevel, difficulty),
            random,
            maximumMonsters);
}
