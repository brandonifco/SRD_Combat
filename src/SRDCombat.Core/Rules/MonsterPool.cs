using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// How completely this engine executes a monster's printed stat block.
/// </summary>
/// <remarks>
/// Graded by <em>where</em> the gap is rather than by how many gaps there are, because
/// the position is what a player would notice. A missing trait is a monster that fights
/// correctly and is slightly weaker than the book; a missing clause on its attack is a
/// monster whose turn does the wrong thing.
/// </remarks>
public enum MonsterCoverage
{
    /// <summary>
    /// No Action entry the engine can resolve. The creature would stand there, so it is
    /// never a fair encounter at any price.
    /// </summary>
    Unusable,

    /// <summary>
    /// It fights, but at least one Action entry loses part of its printed text — the
    /// Boar's Gore without its charge bonus. Correct as far as it goes and quietly
    /// weaker than the stat block.
    /// </summary>
    Diminished,

    /// <summary>
    /// Every Action entry is fully modelled, so the creature's turn is exactly what the
    /// block says; something outside its actions is not — a trait, a Bonus Action, a
    /// Reaction.
    /// </summary>
    Playable,

    /// <summary>Every entry is fully modelled. The engine does exactly what the block says.</summary>
    Complete,
}

/// <summary>
/// Which monsters the encounter builder may draw from, and how completely each one is
/// implemented.
/// </summary>
/// <remarks>
/// <para>
/// The problem this solves: 330 stat blocks is far more than a tier-1 game needs, and a
/// creature whose entries are mostly unmodelled is a poor choice for an authored
/// encounter whatever its CR. Rather than curate a hand-written list that would go stale
/// on every extraction, the pool is <b>derived from the content's own accounting</b> —
/// the same <c>IsFullyModelled</c> the extractor reports on. Implement a trait and the
/// monsters carrying it join the pool at the next regeneration, with nothing to edit
/// here.
/// </para>
/// <para>
/// <b>The admission rule is <see cref="MonsterCoverage.Playable"/> or better</b>: the
/// creature's whole turn is exactly what its stat block prints. That is the line worth
/// drawing because a monster's turn is what the player sees. A <see
/// cref="MonsterCoverage.Diminished"/> creature is still a legal fight and the builder
/// may be told to allow it — <see cref="Admits"/> takes the floor as a parameter — but
/// it is not the default, because "the Boar never charges" is exactly the kind of
/// silent shortfall this project refuses elsewhere.
/// </para>
/// <para>
/// <b>Coverage is not the same as difficulty.</b> Nothing here weights, balances or
/// prices an encounter; <see cref="ChallengeRatingRules"/> owns XP and the SRD's budget
/// table does the balancing. This decides only what belongs in the bag.
/// </para>
/// <para>
/// <b>And coverage is not appropriateness.</b> A Camel is <see
/// cref="MonsterCoverage.Complete"/> and absurd as a foe, which is a third axis owned by
/// <see cref="PlausibleFoes"/> and applied in <see cref="Draw"/> — deliberately there
/// rather than in <see cref="CoverageOf"/> or <see cref="Admits"/>, so that "how much of
/// this creature does the engine execute" keeps exactly one answer.
/// </para>
/// <para>
/// Two CR 0 creatures are <see cref="MonsterCoverage.Unusable"/>, and both are faithful
/// readings rather than coverage gaps — checked against the printed blocks: the Shrieker
/// Fungus has only a Reaction, and the Seahorse only a swim action. The SRD prints
/// creatures that cannot fight, and the pool has to exclude them for that reason rather
/// than pretend they are unimplemented.
/// </para>
/// </remarks>
public static class MonsterPool
{
    /// <summary>
    /// Entry mechanics the engine can actually resolve as a monster's action. An
    /// <c>Unmodelled</c> or <c>Narrative</c> action is not something the creature can do
    /// on its turn, and <c>Reaction</c> is not an action at all.
    /// </summary>
    private static readonly EntryMechanics[] ResolvableActions =
    [
        EntryMechanics.Attack,
        EntryMechanics.Multiattack,
        EntryMechanics.SavingThrow,
    ];

    /// <summary>How completely the engine executes this stat block.</summary>
    public static MonsterCoverage CoverageOf(MonsterDefinition monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        var actions = monster.Entries
            .Where(entry => entry.Section == MonsterEntrySection.Action)
            .ToArray();

        if (!actions.Any(entry => ResolvableActions.Contains(entry.Mechanics)))
        {
            return MonsterCoverage.Unusable;
        }

        if (monster.Entries.All(entry => entry.IsFullyModelled))
        {
            return MonsterCoverage.Complete;
        }

        return actions.All(entry => entry.IsFullyModelled)
            ? MonsterCoverage.Playable
            : MonsterCoverage.Diminished;
    }

    /// <summary>
    /// Whether the encounter builder may use this monster, at or above a coverage floor.
    /// </summary>
    /// <param name="monster">The candidate.</param>
    /// <param name="floor">
    /// The least coverage accepted. Defaults to <see cref="MonsterCoverage.Playable"/>,
    /// the standing rule. Passing <see cref="MonsterCoverage.Unusable"/> does not admit
    /// everything — a creature that cannot act is never admitted, whatever the caller
    /// asks for.
    /// </param>
    public static bool Admits(MonsterDefinition monster, MonsterCoverage floor = MonsterCoverage.Playable)
    {
        var coverage = CoverageOf(monster);

        return coverage != MonsterCoverage.Unusable && coverage >= floor;
    }

    /// <summary>
    /// The monsters an encounter of this tier may draw from, ordered by challenge rating
    /// and then by name so a build is reproducible.
    /// </summary>
    /// <param name="monsters">Every monster in the content.</param>
    /// <param name="maximumChallengeRating">
    /// The hardest creature to include. The gauntlet's tier-1 band is CR 4, which is what
    /// a party of four at levels 1–5 can be asked to fight.
    /// </param>
    /// <param name="floor">The least coverage accepted; see <see cref="Admits"/>.</param>
    /// <param name="plausibleFoesOnly">
    /// Whether to drop the creatures the SRD prints as property rather than as enemies —
    /// see <see cref="PlausibleFoes"/>. True by default, because this is the bag a
    /// <em>random</em> draw comes out of. An authored encounter that really does want a
    /// stampeding elephant passes false, or simply hands <c>EncounterBuilder</c> its own
    /// sequence.
    /// </param>
    public static IReadOnlyList<MonsterDefinition> Draw(
        IEnumerable<MonsterDefinition> monsters,
        decimal maximumChallengeRating,
        MonsterCoverage floor = MonsterCoverage.Playable,
        bool plausibleFoesOnly = true)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        return monsters
            .Where(monster => monster.ChallengeRating <= maximumChallengeRating)
            .Where(monster => Admits(monster, floor))
            .Where(monster => !plausibleFoesOnly || PlausibleFoes.Admits(monster))
            .OrderBy(monster => monster.ChallengeRating)
            .ThenBy(monster => monster.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
