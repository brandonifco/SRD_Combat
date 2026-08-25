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
    /// <param name="traditionalFoesOnly">
    /// Whether to drop the creatures the genre cut excludes — the safari, the
    /// prehistoric menagerie, the bystanders; see <see cref="TraditionalFoes"/>. True by
    /// default for the same reason <paramref name="plausibleFoesOnly"/> is: this is the
    /// bag the <em>random</em> draw comes out of, and an authored fight that wants a
    /// pack of lions passes false.
    /// </param>
    public static IReadOnlyList<MonsterDefinition> Draw(
        IEnumerable<MonsterDefinition> monsters,
        decimal maximumChallengeRating,
        MonsterCoverage floor = MonsterCoverage.Playable,
        bool plausibleFoesOnly = true,
        bool traditionalFoesOnly = true)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        return monsters
            .Where(monster => monster.ChallengeRating <= maximumChallengeRating)
            .Where(monster => Admits(monster, floor))
            .Where(monster => !plausibleFoesOnly || PlausibleFoes.Admits(monster))
            .Where(monster => !traditionalFoesOnly || TraditionalFoes.Admits(monster))
            .OrderBy(monster => monster.ChallengeRating)
            .ThenBy(monster => monster.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The largest footprint, in squares on a side, that this pool can field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pool-level question — <em>what could ever stand on this battlefield</em> —
    /// asked of the same filters that decide admission, so it moves by itself. It answers
    /// 3 today, on the Awakened Tree's Huge space; when coverage growth admits another
    /// Huge creature at the Diminished floor the answer is still derived rather than
    /// edited, and a Gargantuan creature admitted at any challenge rating would take it
    /// to 4 with nothing to change here. A hardcoded 3 would go stale exactly the way a
    /// curated list of pool creatures would have, which is why <see cref="Draw"/> is
    /// derived in the first place (#429, coordination commitment with the battlefield
    /// overhaul).
    /// </para>
    /// <para>
    /// <b>Not the same question as "what is on this field".</b> A built fight knows its
    /// own creatures and sizes anything fight-specific — where terrain may land, how much
    /// room a spawn column needs — from their actual spaces. This is for what has to be
    /// guaranteed <em>before</em> the draw: the width of the routes terrain generation
    /// must leave open, decided when the battlefield is built rather than when it is
    /// populated.
    /// </para>
    /// <para>
    /// Never less than one: an empty pool still has to produce a battlefield somebody can
    /// walk across.
    /// </para>
    /// </remarks>
    /// <param name="monsters">Every monster in the content.</param>
    /// <param name="maximumChallengeRating">The hardest creature to include; see <see cref="Draw"/>.</param>
    /// <param name="floor">The least coverage accepted; see <see cref="Admits"/>.</param>
    /// <param name="plausibleFoesOnly">See <see cref="Draw"/>.</param>
    /// <param name="traditionalFoesOnly">See <see cref="Draw"/>.</param>
    public static int LargestSpan(
        IEnumerable<MonsterDefinition> monsters,
        decimal maximumChallengeRating,
        MonsterCoverage floor = MonsterCoverage.Playable,
        bool plausibleFoesOnly = true,
        bool traditionalFoesOnly = true) =>
        LargestSpan(Draw(monsters, maximumChallengeRating, floor, plausibleFoesOnly, traditionalFoesOnly));

    /// <summary>
    /// The largest footprint among an already-chosen set of creatures — an authored
    /// encounter, or a draw the caller is already holding.
    /// </summary>
    public static int LargestSpan(IEnumerable<MonsterDefinition> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        return chosen
            .Select(monster => CreatureSizeRules.SpaceSpanSquares(
                monster.Sizes.Count > 0 ? monster.Sizes[0] : CreatureSize.Medium))
            .DefaultIfEmpty(1)
            .Max();
    }
}
