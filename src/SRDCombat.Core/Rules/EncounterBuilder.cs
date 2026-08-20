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
    /// The most creatures worth fielding against a party of a given size and level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stated interpretation the SRD does not offer, and the lever that matters most
    /// for whether a fight is survivable. Every additional monster is another whole turn
    /// of attacks each round, so a party outnumbered two to one loses on the action
    /// economy however well it plays.
    /// </para>
    /// <para>
    /// <b>It ignored the party's level until 2026-08-15, and that was the level 1 wall.</b>
    /// "One more creature than there are characters" is a fair cap for a party that can
    /// take a hit and a lethal one for a party that cannot, because the cost of being
    /// outnumbered is not linear in the count — it is paid in *characters removed*. A
    /// level 1 character has 8 to 12 hit points and the creatures a level 1 budget buys
    /// hit for 8 or 9, so very nearly every landed blow drops somebody, and each one takes
    /// a quarter of the party's action economy with it. The same fight at level 5 lands
    /// the same 9 damage on 40 hit points and removes nobody.
    /// </para>
    /// <para>
    /// The measurement that found it: of 120 seeded runs, the ones that died in the first
    /// four fights faced <b>4.2 to 4.6 monsters</b> where the builder's average draw is
    /// 3.0, with the biggest printed hit on the field averaging 8 to 9 — an entire level 1
    /// hit point pool. The budget cannot see this, because <b>XP prices a creature's worth
    /// and not its simultaneity</b>: five creatures costing 60 XP each and one costing 300
    /// are the same purchase to the table on printed page 202 and completely different
    /// fights to four characters with 10 hit points.
    /// </para>
    /// <para>
    /// So the cap grows with the party's capacity to absorb a hit rather than sitting at a
    /// constant: three creatures at level 1, four at level 2, and the original one-more-
    /// than-the-party from level 3 up. The budget is untouched — this spends exactly the
    /// same XP, on fewer and individually dearer creatures, which is the trade the
    /// measurement says a fragile party wants.
    /// </para>
    /// </remarks>
    /// <param name="partySize">How many characters are in the fight.</param>
    /// <param name="partyLevel">
    /// The level to size against. The *lowest* in the party, since the cap exists to stop
    /// the frailest member being removed before they act.
    /// </param>
    public static int MaximumFor(int partySize, int partyLevel) =>
        Math.Clamp(Math.Min(partySize + 1, partyLevel + 2), 1, DefaultMaximumMonsters);

    /// <summary>
    /// The fewest creatures worth fielding against a party of a given size and level.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="MaximumFor"/>, added when the back half's flatness was
    /// measured (2026-08-15). The count draw was uniform over 1..max, so two fights in
    /// five put one or two creatures against four coordinated characters — and those
    /// fights are free: measured over 120 seeded runs, single-creature fights ended with
    /// the party at <b>89%</b> of its hit points and two-creature fights at 83%, against
    /// 70% for five, because focus fire deletes a lone creature's whole action economy
    /// before it has taken two turns. The budget cannot see this for the same reason it
    /// could not see the level 1 wall: XP prices worth, not simultaneity. So once the
    /// party can absorb being outnumbered — the same level 3 boundary the cap uses — a
    /// fight fields at least two creatures. Level 1 keeps its floor of one: the fragile
    /// opening was fixed by lowering its ceiling, and raising its floor would claw that
    /// back.
    /// </remarks>
    /// <param name="partySize">How many characters are in the fight.</param>
    /// <param name="partyLevel">The level to size against — the party's lowest.</param>
    public static int MinimumFor(int partySize, int partyLevel) =>
        partyLevel >= 3 && partySize >= 3 ? 2 : 1;

    /// <summary>Builds an encounter to a budget from a pool of candidates.</summary>
    /// <param name="candidates">The monsters that may be used. Usually a <see cref="MonsterPool"/> draw.</param>
    /// <param name="budget">The XP to spend, from <see cref="EncounterBudget.For"/>.</param>
    /// <param name="random">The dice. Determinism is what makes a fight reproducible from its seed.</param>
    /// <param name="maximumMonsters">The most creatures to field.</param>
    /// <param name="minimumMonsters">
    /// The fewest to aim for. A target, not a guarantee — a budget too small to buy two
    /// of anything still fields what it can afford.
    /// </param>
    public static BuiltEncounter Build(
        IEnumerable<MonsterDefinition> candidates,
        int budget,
        IRandomSource random,
        int maximumMonsters = DefaultMaximumMonsters,
        int minimumMonsters = 1)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMonsters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumMonsters, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumMonsters, maximumMonsters);

        // Ordered before anything is chosen: the caller's sequence might be a LINQ query
        // whose order is incidental, and a reproducible fight cannot rest on that.
        var affordable = candidates
            .Where(monster => monster.ExperiencePoints > 0)
            .OrderBy(monster => monster.ExperiencePoints)
            .ThenBy(monster => monster.Id, StringComparer.Ordinal)
            .ToArray();

        // How many creatures, decided before which ones. Roll(n) returns 1..n, so this
        // draws uniformly over minimum..maximum.
        var targetCount = minimumMonsters + random.Roll(maximumMonsters - minimumMonsters + 1) - 1;

        var chosen = new List<MonsterDefinition>();
        var remaining = budget;

        for (var slot = 0; slot < targetCount; slot++)
        {
            var share = remaining / (targetCount - slot);

            // The fight's composition has to make sense: the first pick anchors it,
            // and every later slot draws only companions of that anchor — see
            // EncounterThemes for the map and the anchor reading. Two refinements
            // decided here rather than there. A fight of five or more never anchors on
            // a loner, because "a horde of Owlbears" is a budget accident where "a
            // horde of goblins" is a warband — the loner still headlines the smaller
            // fights its budget was buying anyway. And a slot that prices out of
            // companions ends the fight short rather than seating a stranger, the
            // same under-spend the thin-budget path already accepts.
            var options = affordable
                .Where(monster => monster.ExperiencePoints <= remaining)
                .Where(monster => chosen.Count == 0
                    ? targetCount < 5 || EncounterThemes.KeepsCompany(monster)
                    : EncounterThemes.Companions(chosen[0], monster))
                .ToArray();

            // An anchor must exist whatever the pool holds: a caller whose affordable
            // candidates are somehow all loners still gets a fight, not an empty one.
            if (options.Length == 0 && chosen.Count == 0)
            {
                options = affordable.Where(monster => monster.ExperiencePoints <= remaining).ToArray();
            }

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
            //
            // The band is chosen by PRICE and not by count, and that distinction is
            // worth the two extra lines. Taking the first third of a list sorted by
            // price and then by identifier cuts through the middle of a tie: where a
            // dozen creatures cost the same, only the alphabetically earliest entered
            // the band and the rest could never be drawn at all. Measured before this
            // change, 6,000 generated encounters fielded **68 distinct creatures out of
            // a pool of 117** — a bestiary half of which was unreachable — and the most
            // frequently drawn read Ankheg, Archelon, Azer, Awakened, Bandit, Basilisk,
            // which is not a coincidence but an alphabet.
            //
            // So the count only decides the price to beat, and everything at that price
            // comes with it. Nothing about "spend most of the budget" is given up: every
            // creature admitted still costs at least what the count would have demanded.
            var byPrice = pool
                .OrderByDescending(monster => monster.ExperiencePoints)
                .ThenBy(monster => monster.Id, StringComparer.Ordinal)
                .ToArray();

            // The identifier still orders what survives, because PickByTaste walks the
            // candidates accumulating weight and a seed has to replay exactly. It is
            // only the *cut* that must not fall inside a tie.
            var cheapestWorthTaking = byPrice[Math.Max(1, byPrice.Length / 3) - 1].ExperiencePoints;

            var dearest = byPrice
                .Where(monster => monster.ExperiencePoints >= cheapestWorthTaking)
                .ToArray();

            var pick = PickByTaste(dearest, random);

            chosen.Add(pick);
            remaining -= pick.ExperiencePoints;
        }

        return new BuiltEncounter(chosen, budget, budget - remaining);
    }

    /// <summary>
    /// How much likelier a creature of any other type is than a <c>Beast</c> when a slot
    /// is filled. One means no preference at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is taste, not a rule, and it is the fourth axis.</b> Coverage says what
    /// the engine can run, the budget says how much a fight costs, and
    /// <see cref="PlausibleFoes"/> says what the SRD prints as property rather than as
    /// an enemy. None of them has an opinion about <em>genre</em>, so a correctly built,
    /// perfectly plausible fight kept opening with an Ape — reported from play on
    /// 2026-08-16 as "I don't like fighting apes or other random wild animals".
    /// </para>
    /// <para>
    /// Derived from the book's own taxonomy rather than a hand-written list of names:
    /// <c>Beast</c> is the printed creature type for ordinary animals, so the whole
    /// preference is one enum comparison and a renamed stat block cannot silently
    /// escape it. That does sweep up the animals that <em>are</em> genre-appropriate —
    /// a wolf pack, a giant spider — which is the stated cost of deriving instead of
    /// curating, and the reason this is a weight rather than an exclusion.
    /// </para>
    /// <para>
    /// <b>A weight, never a filter</b>, and the pool's shape is why: of 117 creatures at
    /// CR 4 and below, 46 are Beasts, and at CR 0 nineteen of twenty-two are. Excluding
    /// them would leave three creatures at the bottom of the ladder and break the pool's
    /// own "at least five at every CR" floors. Weighting starves nothing — where a slot's
    /// candidates are all Beasts, a Beast is still chosen.
    /// </para>
    /// <para>
    /// Three is a chosen number, not a derived one. Measured over 6,000 built
    /// encounters at all three difficulties, it takes the Beast share of the creatures
    /// actually drawn from <b>43.1% to 23.7%</b> — "sometimes there are wolves" rather
    /// than a wildlife documentary. It is the one knob here worth turning if that
    /// judgement is wrong, and the measurement is a two-line change to re-run: flip
    /// this constant to 1 for the unweighted baseline.
    /// </para>
    /// </remarks>
    public const int ClassicMonsterWeight = 3;

    /// <summary>The draw weight for one candidate.</summary>
    private static int TasteWeight(MonsterDefinition monster) =>
        monster.Type == CreatureType.Beast ? 1 : ClassicMonsterWeight;

    /// <summary>
    /// Chooses one candidate, favouring the classic fantasy bestiary over ordinary
    /// animals. Spends exactly one roll however long the list is, so a scripted-dice
    /// test scripts the same number of dice it always did.
    /// </summary>
    private static MonsterDefinition PickByTaste(
        IReadOnlyList<MonsterDefinition> candidates,
        IRandomSource random)
    {
        var total = candidates.Sum(TasteWeight);
        var roll = random.Roll(total);
        var running = 0;

        foreach (var candidate in candidates)
        {
            running += TasteWeight(candidate);

            if (roll <= running)
            {
                return candidate;
            }
        }

        return candidates[^1];
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
        int? maximumMonsters = null,
        int? minimumMonsters = null)
    {
        ArgumentNullException.ThrowIfNull(partyLevels);

        var levels = partyLevels.ToArray();
        var lowest = levels.Length == 0 ? 1 : levels.Min();
        var maximum = maximumMonsters ?? MaximumFor(levels.Length, lowest);

        return Build(
            candidates,
            EncounterBudget.ForLevels(levels, difficulty),
            random,
            // The lowest level in the party, not the average: a fight sized against the
            // mean would still be sized to remove the character least able to survive it,
            // and a party diverges the moment somebody dies and stops earning.
            maximum,
            Math.Min(minimumMonsters ?? MinimumFor(levels.Length, lowest), maximum));
    }
}
