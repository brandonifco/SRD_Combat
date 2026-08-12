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
/// <c>maximumMonsters</c> caps it.
/// </para>
/// <para>
/// <b>The count is chosen before the creatures, and that is a correction.</b> The first
/// version picked uniformly among everything affordable and repeated until the budget ran
/// out, which sounds even-handed and is not: a cheap creature is affordable at every step,
/// so the process filled the cap with them. Measured over 200 seeds, a low-difficulty
/// fight for four level 1 characters came to <b>5.4 creatures on average and hit the cap
/// a quarter of the time</b> — six to eight monsters against four characters, which is an
/// action-economy problem no amount of tactics solves, and it is not what the SRD's
/// examples look like. Deciding the count first and then spending the budget across it
/// reproduces the printed range: one big creature, a pair, or a swarm.
/// </para>
/// </remarks>
public static class EncounterBuilder
{
    /// <summary>How many monsters an encounter may contain unless told otherwise.</summary>
    public const int DefaultMaximumMonsters = 8;

    /// <summary>
    /// The most creatures worth fielding against a party of a given size.
    /// </summary>
    /// <remarks>
    /// A stated interpretation the SRD does not offer, and the lever that matters most
    /// for whether a fight is survivable. Every additional monster is another whole turn
    /// of attacks each round, so a party outnumbered two to one loses on the action
    /// economy however well it plays. One more creature than there are characters keeps
    /// a fight tense without making it arithmetic.
    /// </remarks>
    public static int MaximumFor(int partySize) =>
        Math.Clamp(partySize + 1, 1, DefaultMaximumMonsters);

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

        // How many creatures, decided before which ones. Roll(n) returns 1..n.
        var targetCount = random.Roll(maximumMonsters);

        var chosen = new List<MonsterDefinition>();
        var remaining = budget;

        for (var slot = 0; slot < targetCount; slot++)
        {
            var share = remaining / (targetCount - slot);

            var options = affordable
                .Where(monster => monster.ExperiencePoints <= remaining)
                .ToArray();

            if (options.Length == 0)
            {
                break;
            }

            // A slot is worth roughly its share of what is left: at least half of it, so
            // the encounter is not filled with the cheapest creature in the book, and no
            // more than all of it, so one slot does not swallow the whole budget and
            // leave the rest empty. Both bounds are needed — a floor alone produces a
            // swarm of rats, a ceiling alone produces a single monster every time.
            var withinShare = options
                .Where(monster => monster.ExperiencePoints <= share && monster.ExperiencePoints * 2 >= share)
                .ToArray();

            var pool = withinShare.Length > 0
                ? withinShare
                : options.Where(monster => monster.ExperiencePoints <= share).ToArray();

            // Nothing is small enough for a slot's share, which happens when the budget
            // is thin: take the cheapest thing that fits at all rather than nothing.
            if (pool.Length == 0)
            {
                pool = [options[0]];
            }

            // From the dearer end of what fits the slot. "Spend as much of your XP
            // budget as you can without going over" is the printed instruction, and
            // picking flatly across the band leaves a fight measurably under budget.
            var dearest = pool
                .OrderByDescending(monster => monster.ExperiencePoints)
                .ThenBy(monster => monster.Id, StringComparer.Ordinal)
                .Take(Math.Max(1, pool.Length / 3))
                .ToArray();

            var pick = dearest[random.Roll(dearest.Length) - 1];

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

    /// <summary>
    /// Builds an encounter for a party whose members may be at different levels.
    /// </summary>
    public static BuiltEncounter ForLevels(
        IEnumerable<MonsterDefinition> candidates,
        IEnumerable<int> partyLevels,
        EncounterDifficulty difficulty,
        IRandomSource random,
        int? maximumMonsters = null)
    {
        ArgumentNullException.ThrowIfNull(partyLevels);

        var levels = partyLevels.ToArray();

        return Build(
            candidates,
            EncounterBudget.ForLevels(levels, difficulty),
            random,
            maximumMonsters ?? MaximumFor(levels.Length));
    }
}
