using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>One rung of the ladder: a fight at a difficulty, and the rest before it.</summary>
/// <remarks>
/// A rung names no level, deliberately. It used to, and that made the ladder *grant*
/// levels on a schedule; now experience does, so a rung says only how hard the fight
/// should be for whoever turns up to it.
/// </remarks>
/// <param name="Difficulty">How hard it should be.</param>
/// <param name="RestBefore">The rest the party gets before it, if any.</param>
/// <param name="Objective">
/// What wins it, or null for last-side-standing. A *spec* rather than an
/// <see cref="EncounterObjective"/> because a rung is written before its monsters exist,
/// and "kill the leader" cannot name a leader until there is one to mark —
/// <see cref="EncounterFactory"/> resolves it at build time.
/// </param>
/// <param name="Horde">
/// Whether this rung is a warband: many cheap creatures instead of a few dear ones, on
/// the same printed budget. Ignored below level 3 — see
/// <c>EncounterFactory.HordeMinimum</c> for why the fragile tier is exempt.
/// </param>
public sealed record LadderStep(
    EncounterDifficulty Difficulty,
    RestKind? RestBefore = null,
    ObjectiveSpec? Objective = null,
    bool Horde = false);

/// <summary>
/// A rung's objective before it has a battlefield: the kind, and the number a
/// <see cref="ObjectiveKind.SurviveRounds"/> needs.
/// </summary>
/// <remarks>
/// The split exists because a ladder is authored ahead of the fights it describes. This
/// says "this rung is a hold-the-line fight"; <see cref="EncounterFactory"/> turns it into
/// an <see cref="EncounterObjective"/> once there are monsters to mark and a side to own it.
/// </remarks>
/// <param name="Kind">What ends the fight.</param>
/// <param name="Rounds">Rounds to survive, for <see cref="ObjectiveKind.SurviveRounds"/>.</param>
public sealed record ObjectiveSpec(ObjectiveKind Kind, int Rounds = 0)
{
    /// <summary>Outlast the enemy for a stated number of rounds.</summary>
    public static ObjectiveSpec Survive(int rounds) => new(ObjectiveKind.SurviveRounds, rounds);

    /// <summary>Kill the toughest creature on the field and the rest break off.</summary>
    public static ObjectiveSpec KillLeader { get; } = new(ObjectiveKind.KillLeader);
}

/// <summary>How a run ended, or that it has not.</summary>
public enum RunOutcome
{
    /// <summary>Still climbing.</summary>
    InProgress,

    /// <summary>Every rung cleared.</summary>
    Survived,

    /// <summary>The party was wiped out.</summary>
    Defeated,
}

/// <summary>
/// The ladder of fights a run climbs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Data, deliberately.</b> The default ladder below is generated to a shape, but a
/// ladder is just a list of rungs, so an authored one — a fixed sequence of hand-picked
/// encounters — drops in without the run knowing the difference. That matters because
/// authored groups are the expected fix for #52, where a randomly drawn encounter can
/// field a Camel.
/// </para>
/// <para>
/// The default shape is a stated design choice rather than anything the SRD prints — the
/// book defines difficulties, not sequences of them. The choice was measured before it
/// was made (#65): with High in the routine rotation every third fight, 38 of 40 seeded
/// runs died on a Moderate or High rung, 23 of them on High, at a median of three fights
/// cleared of thirty, while a ladder of Low and Moderate alone reached a median of
/// seven. High is exactly what the book says it is — "could be lethal for one or more
/// characters" — so it is served as a set piece rather than a routine: four routine
/// fights alternating Low and Moderate, then a High milestone closing each cycle of
/// five, entered fresh off a Long Rest.
/// </para>
/// <para>
/// Short Rests between the routine fights give the short-rest resources — a Barbarian's
/// Rage, a Fighter's Second Wind — something to be scarce *for*, which a ladder of long
/// rests would quietly remove. The two Long Rests per cycle bracket the milestone: one
/// before it, so the lethal fight is fought at full strength rather than on a cycle's
/// accumulated attrition (which is what the measurement showed was ending runs), and one
/// after it, opening the next cycle — which is also where the fallen usually rejoin,
/// since deaths cluster on the High rung and a Long Rest is what brings them back.
/// </para>
/// </remarks>
public static class GauntletLadder
{
    /// <summary>Fights in one cycle: four routine rungs and a High milestone.</summary>
    public const int FightsPerCycle = 5;

    /// <summary>
    /// The default ladder: cycles of Low, Moderate, Low, Moderate and then a High
    /// milestone, until the run is long enough to carry a party from level 1 to level 5.
    /// </summary>
    /// <remarks>
    /// The length is chosen against the arithmetic rather than picked: a cycle awards
    /// each character two low, two moderate and one high per-character budget — at
    /// level 1 that is 350 XP against the old three-fight cycle's 225, near enough the
    /// same per fight — and reaching level 5 costs 6,500 XP, so a run still needs
    /// roughly thirty fights, arriving at level 5 in the final cycle.
    /// </remarks>
    public static IReadOnlyList<LadderStep> Default(int fights = 30)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fights, 1);

        return Enumerable.Range(0, fights)
            .Select(index =>
            {
                var slot = index % FightsPerCycle;

                // No rest before the first fight; Long Rests bracket each High milestone
                // — before it and at the start of the next cycle — with Short Rests
                // between the routine fights.
                //
                // The opening cycle is the exception, and it rests Long throughout.
                // Reported from play on 2026-08-16 as "level 1 characters die too
                // quick, especially for the first few matches", and the instrument
                // agreed: died-by-fight-4 was the largest single failure cohort in the
                // whole run.
                //
                // The cause is not the ladder's difficulty and not the budget. A level
                // 1 character has exactly ONE Hit Die, a Short Rest spends it, and Hit
                // Dice come back only on a Long Rest — so the party got one real heal
                // per five-fight cycle and then fought fights 2, 3 and 4 on whatever
                // was left. Meanwhile the XP budget prices every one of those fights
                // for a party at full strength, because it cannot see hit points.
                //
                // Resting Long here costs no fidelity at all: how often a party rests
                // is the GM's call, which is to say this project's, exactly like
                // LootTable's award rates. Nothing about what a rest *restores* moves.
                // Tied to the cycle rather than to party level because the ladder is
                // built once and never sees a level — and by its own XP arithmetic the
                // opening cycle is levels 1-2, which is the tier meant.
                //
                // Measured on two seed ranges against a same-build baseline:
                // died-by-fight-4 15 -> 1 (seeds 1-120) and 14 -> 6 (seeds 200-320),
                // with the opening band's hit points left rising 78% -> 87% and 79% ->
                // 85%. Two weaker variants were measured and rejected — one extra Long
                // Rest at fight 3, and a Hit Die returned on Short Rests at levels 1-2
                // — because this beat both on early deaths *and* on how much it
                // inflated the back half.
                //
                // That inflation is the honest cost and it is not a defect of this
                // change: full clears rise 66 -> 72 and 71 -> 78, because more runs
                // survive to reach an ending that is already too easy (#192). Every
                // variant tried did it, by the same mechanism. The opening and the
                // ending are one problem wearing two faces, and the ending needs its
                // own teeth rather than a lethal first cycle standing in for them.
                RestKind? rest = index == 0
                    ? null
                    : index < FightsPerCycle
                        ? RestKind.Long
                        : slot == 0 || slot == FightsPerCycle - 1
                            ? RestKind.Long
                            : RestKind.Short;

                // Two rungs of every five are not deathmatches, which is the whole point:
                // thirty fights with one identical goal is one fight played thirty times.
                // The High milestone becomes a boss fight — the dearest creature on the
                // field is marked and the rest break off when it drops — and one routine
                // Low rung becomes a holding action. The other three are unchanged, so a
                // cycle still asks for a straight kill more often than not.
                var objective = slot switch
                {
                    2 => ObjectiveSpec.Survive(3),
                    FightsPerCycle - 1 => ObjectiveSpec.KillLeader,
                    _ => null,
                };

                return new LadderStep(
                    slot switch
                    {
                        // The warband rung is budgeted Low, not Moderate, and this is
                        // the difference between a rung and a wall. At a Moderate
                        // budget spread over six to ten bodies the fight is simply
                        // unwinnable: measured, it took full clears from 72 of 120 to
                        // **12**, because the per-count numbers are a cliff rather than
                        // a slope — five creatures leave the party at 75% of its hit
                        // points and down 0.25 of a character, six leave it at 51% and
                        // down 1.11. Crossing from five to six is worth more than every
                        // difficulty step on the ladder.
                        //
                        // A Low budget across many bodies is also the honest shape of
                        // the thing: a goblin warband is not a deadly enemy, it is a lot
                        // of weak ones. Same printed budget rules, same spend, just
                        // divided further.
                        0 or 2 or 3 => EncounterDifficulty.Low,
                        1 => EncounterDifficulty.Moderate,
                        _ => EncounterDifficulty.High,
                    },
                    rest,
                    objective,
                    // The fourth rung is a warband. It is the third distinct *shape* in
                    // a cycle, after the holding action and the boss, and shapes are the
                    // answer to this ladder's oldest problem: thirty fights built from
                    // one five-rung cycle repeated six times means a player has seen the
                    // whole run by the end of fight 5. Difficulty alone cannot fix that,
                    // because the budget re-prices every fight against the party's level
                    // — bigger numbers on both sides is no change at all.
                    //
                    // It is also the one kind of difficulty the budget structurally
                    // cannot price. "XP prices worth, not simultaneity" is a finding
                    // this project has now reached from four directions: the level 1
                    // wall, the count floor, the boss escort, and the per-count
                    // measurement where a lone creature leaves the party at 89% of its
                    // hit points and five leave it at 70%. A warband spends the same
                    // printed XP across many bodies, so it is harder without being
                    // richer, and no printed rule is bent to do it.
                    Horde: slot == 3);
            })
            .ToArray();
    }
}

/// <summary>
/// A run through the gauntlet: the party, the ladder, and what is left of both.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a sequence of fights a game. The party carries wounds, spent
/// resources and its dead from one rung to the next, rests restore what the printed rules
/// say they restore, and a wipe ends the run.
/// </para>
/// <para>
/// <b>The run owns the state; the engine owns the fight.</b> Each rung builds fresh
/// combatants seeded from <see cref="CharacterState"/>, and the state is read back when
/// the fight ends. Nothing about a run leaks into <c>Encounter</c>, which stays a single
/// self-contained fight exactly as the frozen transcripts need it to be.
/// </para>
/// <para>
/// Levelling is deliberately absent: rungs name the level they are built for, and the
/// party is rebuilt at that level between rungs, but awarding XP and deciding when a
/// party has earned a level is its own piece of work.
/// </para>
/// </remarks>
public sealed class GauntletRun
{
    private readonly SrdContent _content;
    private readonly List<CharacterState> _states;
    private readonly List<string> _casualties = [];
    private readonly List<string> _levelUps = [];
    private readonly List<string> _returns = [];
    private readonly List<string> _lootFound = [];

    /// <summary>
    /// Whether a plan-less draft that reaches an Ability Score Improvement should get
    /// <see cref="ResolveMember"/>'s default rather than the plain resolve.
    /// </summary>
    /// <remarks>
    /// <b>Scoped deliberately to the created and resumed entry points, not the plain
    /// pregenerated one.</b> The pregenerated party's own drafts carry no plan either
    /// (#330, found while building this) — the same gap this field's fallback exists
    /// to close — but every seed range <c>PacingMeasure</c> and the baseline gameplay
    /// tests measure walks the plain <see cref="Start(SrdContent, IReadOnlyList{LadderStep}?, int)"/>
    /// path, so defaulting there would move numbers nobody asked this change to move
    /// (and, found the hard way, can turn a fight one of the ten-seed loot-run tests
    /// relies on into a stall — the pre-existing, separately tracked #256). A created
    /// party's own drafts always carry a real plan once creation asks for one, so this
    /// flag only ever matters for the honest fallback on a draft that has none — which
    /// is exactly the created/resumed shape #288 is about.
    /// </remarks>
    private readonly bool _defaultsPlanlessAsi;

    private GauntletRun(
        SrdContent content,
        IReadOnlyList<LadderStep> ladder,
        IReadOnlyList<PartyMember> party,
        IReadOnlyList<CharacterState> states,
        bool defaultsPlanlessAsi = false)
    {
        _content = content;
        _states = [.. states];
        Ladder = ladder;
        Party = party;
        _defaultsPlanlessAsi = defaultsPlanlessAsi;
    }

    /// <summary>Starts a run with the pregenerated party.</summary>
    public static GauntletRun Start(
        SrdContent content,
        IReadOnlyList<LadderStep>? ladder = null,
        int startingLevel = 1)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Start(content, PregeneratedParty.Build(content, startingLevel), ladder);
    }

    /// <summary>
    /// Starts a run with a created party — the drafts a creation flow built. Everything
    /// downstream is indifferent to where a draft came from: the save carries drafts
    /// whoever wrote them, levelling re-resolves them, and defeat-means-reload needs
    /// nothing new.
    /// </summary>
    public static GauntletRun Start(
        SrdContent content,
        IReadOnlyList<CharacterDraft> drafts,
        IReadOnlyList<LadderStep>? ladder = null,
        int startingLevel = 1)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentOutOfRangeException.ThrowIfZero(drafts.Count);

        var resolved = drafts
            .Select((draft, index) => ResolveMember(content, draft, startingLevel, x: 0, y: index))
            .ToArray();

        var run = Start(content, [.. resolved.Select(pair => pair.Member)], ladder, defaultsPlanlessAsi: true);
        run._levelUps.AddRange(resolved.Select(pair => pair.AsiDefaultNotice).OfType<string>());

        return run;
    }

    private static GauntletRun Start(
        SrdContent content,
        IReadOnlyList<PartyMember> party,
        IReadOnlyList<LadderStep>? ladder,
        bool defaultsPlanlessAsi = false)
    {
        var rungs = ladder ?? GauntletLadder.Default();

        if (rungs.Count == 0)
        {
            throw new ArgumentException("A run needs at least one rung.", nameof(ladder));
        }

        return new GauntletRun(
            content,
            rungs,
            party,
            [.. party.Select(CharacterState.Fresh)],
            defaultsPlanlessAsi);
    }

    /// <summary>
    /// Resumes a saved run: each draft re-resolved at the level its experience has
    /// earned, never at the level the file claims.
    /// </summary>
    /// <remarks>
    /// The party comes back exactly as strong as the rules make it from what was saved —
    /// a save holds no derived numbers, so there is nothing on disk for the rules to
    /// disagree with. Validation of the file itself is <see cref="RunSave.FromJson"/>'s;
    /// this trusts its argument the way <see cref="Start"/> trusts the content.
    /// </remarks>
    public static GauntletRun Resume(SrdContent content, SavedRun saved)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(saved);

        var resolved = saved.Members
            .Select((member, index) =>
                ResolveMember(content, member.Draft, member.State.Level, x: 0, y: index))
            .ToArray();

        var run = new GauntletRun(
            content,
            saved.Ladder,
            [.. resolved.Select(pair => pair.Member)],
            [.. saved.Members.Select(member => member.State)],
            defaultsPlanlessAsi: true)
        {
            Cleared = saved.Cleared,
            GoldCopper = saved.GoldCopper,
        };

        run._casualties.AddRange(saved.Casualties);
        run._levelUps.AddRange(resolved.Select(pair => pair.AsiDefaultNotice).OfType<string>());

        if (saved.Cleared >= saved.Ladder.Count)
        {
            run.Outcome = RunOutcome.Survived;
        }

        return run;
    }

    /// <summary>The saveable snapshot of this run: drafts plus progress, nothing derived.</summary>
    public SavedRun ToSave() => new()
    {
        FormatVersion = RunSave.CurrentFormatVersion,
        Ladder = Ladder,
        Cleared = Cleared,
        Members = [.. Party.Zip(_states, (member, state) => new SavedMember(member.Draft, state))],
        Casualties = [.. _casualties],
        GoldCopper = GoldCopper,
    };

    /// <summary>The rungs, in order.</summary>
    public IReadOnlyList<LadderStep> Ladder { get; }

    /// <summary>The party, resolved at the level of the rung they are on.</summary>
    public IReadOnlyList<PartyMember> Party { get; private set; }

    /// <summary>What each member is carrying, in the same order as <see cref="Party"/>.</summary>
    public IReadOnlyList<CharacterState> States => _states;

    /// <summary>How many rungs have been cleared.</summary>
    public int Cleared { get; private set; }

    /// <summary>
    /// The party's shared purse, in copper so every printed price is exact. Gold is a
    /// party resource rather than a per-member one because its only use is the Long
    /// Rest shop, and the shop equips whoever the purchase improves.
    /// </summary>
    public int GoldCopper { get; private set; }

    /// <summary>How the run ended, or that it has not.</summary>
    public RunOutcome Outcome { get; private set; } = RunOutcome.InProgress;

    /// <summary>Names of the characters who died, in the order they fell.</summary>
    public IReadOnlyList<string> Casualties => _casualties;

    /// <summary>The rung about to be fought, or null when the run is over.</summary>
    public LadderStep? Next => Outcome == RunOutcome.InProgress && Cleared < Ladder.Count
        ? Ladder[Cleared]
        : null;

    /// <summary>
    /// Takes the rest the next rung offers, and rebuilds the party at its level.
    /// </summary>
    /// <returns>The rest taken, or null if the rung offers none.</returns>
    public RestKind? PrepareForNext(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (Next is not { } step)
        {
            return null;
        }

        if (step.RestBefore is not { } rest)
        {
            return null;
        }

        for (var i = 0; i < _states.Count; i++)
        {
            var before = _states[i];

            _states[i] = before.AfterRest(
                Party[i],
                rest,
                random,
                _content.ClassesById[Party[i].Draft.ClassId].HitDieSides);

            if (before.IsDead && !_states[i].IsDead)
            {
                _returns.Add($"{Party[i].Draft.Name} rejoins the party");
            }
        }

        return rest;
    }

    /// <summary>Builds the next fight, with the party carrying everything it has left.</summary>
    public Fight BeginNext(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var step = Next ?? throw new InvalidOperationException("The run is over.");

        // The dead stay out of it, and the encounter is budgeted for whoever is left —
        // "multiply the number in the table by the number of characters in the party"
        // counts the characters actually in the fight. A party that loses someone gets
        // smaller fights, which is the printed rule rather than a mercy.
        var survivors = Party
            .Select((member, index) => (member, state: _states[index]))
            .Where(pair => pair.state.CanFight)
            .Select(pair => pair.member.CarryingOver(new CombatantCarryOver(
                pair.state.CurrentHitPoints,
                pair.state.RagesRemaining,
                pair.state.SecondWindRemaining,
                pair.state.ActionSurgeRemaining,
                pair.state.SpellSlotsRemaining,
                pair.state.Potions,
                pair.state.ChannelDivinityRemaining)))
            .ToArray();

        return EncounterFactory.Build(
            _content,
            survivors,
            step.Difficulty,
            random,
            objective: step.Objective,
            horde: step.Horde);
    }

    /// <summary>
    /// Records a finished fight: reads the survivors' state back, advances the ladder,
    /// and — when the cleared rung was a High milestone and a random source is given —
    /// rolls the milestone's loot.
    /// </summary>
    /// <param name="fight">The finished fight.</param>
    /// <param name="random">
    /// The source loot is rolled on. Null means no loot, which keeps a caller that
    /// wants only the bookkeeping — most tests — unchanged.
    /// </param>
    public void CompleteFight(Fight fight, IRandomSource? random = null)
    {
        ArgumentNullException.ThrowIfNull(fight);

        if (!fight.Encounter.IsComplete)
        {
            throw new InvalidOperationException("That fight has not finished.");
        }

        foreach (var combatant in fight.Encounter.Combatants
                     .Where(combatant => combatant.SideId == PregeneratedParty.SideId))
        {
            var index = Party.ToList().FindIndex(member => member.Combatant.Id == combatant.Id);

            if (index < 0)
            {
                continue;
            }

            var before = _states[index];
            var after = before.AfterFight(combatant);

            if (after.IsDead && !before.IsDead)
            {
                _casualties.Add(Party[index].Draft.Name);
            }

            _states[index] = after;
        }

        if (_states.All(state => !state.CanFight))
        {
            Outcome = RunOutcome.Defeated;
            return;
        }

        if (fight.Encounter.WinningSide != PregeneratedParty.SideId)
        {
            Outcome = RunOutcome.Defeated;
            return;
        }

        AwardExperience(fight);
        AwardGold(fight);

        var step = Ladder[Cleared];
        Cleared++;

        if (Cleared >= Ladder.Count)
        {
            Outcome = RunOutcome.Survived;
        }

        // The set piece pays out a permanent item; a Moderate fight pays a potion. Both
        // rates are stated design choices — see the remarks on LootTable.
        if (random is null)
        {
            return;
        }

        if (step.Difficulty == EncounterDifficulty.High)
        {
            AwardLoot(random);
        }
        else if (step.Difficulty == EncounterDifficulty.Moderate)
        {
            AwardPotion(random);
        }
    }

    /// <summary>
    /// Hands a Potion of Healing to whoever is carrying the fewest.
    /// </summary>
    /// <remarks>
    /// Spread rather than rolled, deliberately: potions are only worth carrying if
    /// somebody who can reach the wounded has one, and piling a run's whole supply on
    /// one character is how a party ends up watching an ally bleed out three squares
    /// from the potions. The roll breaks the tie, so the choice stays seed-reproducible.
    /// </remarks>
    private void AwardPotion(IRandomSource random)
    {
        var candidates = _states
            .Select((state, index) => (state, index))
            .Where(pair => pair.state.CanFight)
            .GroupBy(pair => pair.state.Potions.Values.Sum())
            .OrderBy(group => group.Key)
            .First()
            .ToArray();

        if (candidates.Length == 0)
        {
            return;
        }

        var (_, chosen) = candidates[random.Roll(candidates.Length) - 1];
        var potency = LootTable.PotionFor(_states[chosen].Level);

        _states[chosen] = _states[chosen].Carrying(potency);
        _lootFound.Add($"{Party[chosen].Draft.Name} finds a {PotionRules.PrintedName(potency)}");
    }

    /// <summary>Rolls the milestone's drop and equips it, re-resolving the finder.</summary>
    private void AwardLoot(IRandomSource random)
    {
        if (LootTable.Roll(_content, Party, _states, random) is not { } award)
        {
            return;
        }

        var party = Party.ToArray();
        var index = award.MemberIndex;
        var before = party[index].Sheet.MaximumHitPoints;

        // Equipping is a draft change and a re-resolve, never a sheet edit — the same
        // rule levelling follows, and for the same reason.
        party[index] = PregeneratedParty.Resolve(
            _content,
            award.NewDraft,
            _states[index].Level,
            x: 0,
            y: index);

        // An item can raise the hit point maximum (the Amulet of Health); the extra
        // arrives as extra hit points, not as healing, exactly like a level's.
        var gained = Math.Max(0, party[index].Sheet.MaximumHitPoints - before);

        if (gained > 0)
        {
            _states[index] = _states[index] with
            {
                CurrentHitPoints = _states[index].CurrentHitPoints + gained,
            };
        }

        Party = party;
        _lootFound.Add($"{party[index].Draft.Name} finds {award.Description}");
    }

    /// <summary>
    /// Awards the fight's winnings: one gold piece per ten points of the defeated
    /// monsters' printed XP.
    /// </summary>
    /// <remarks>
    /// A stated design rate, like the loot table's — the SRD prints monster XP and
    /// equipment prices but no link between them ("contains treasure of the GM's
    /// choice"). One-tenth calibrates the Equipment chapter's own price ladder to the
    /// run: a level 1 cycle's ~1,400 XP buys a suit of Chain Mail or a couple of
    /// potions, and Plate's 1,500 GP stays a whole run's ambition.
    /// </remarks>
    private void AwardGold(Fight fight) =>
        GoldCopper += fight.Built.Monsters.Sum(monster => monster.ExperiencePoints) * 10;

    /// <summary>
    /// Buys one <see cref="ShopOffer"/>: the purse pays the printed price, and the
    /// gear arrives as a draft change re-resolved — the loot award's own pattern —
    /// or the potion goes into the named member's pack.
    /// </summary>
    /// <returns>Null on success, or the refusal — an empty purse refuses cleanly.</returns>
    public ActionRefusal? Purchase(ShopOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (offer.CostCopper > GoldCopper)
        {
            return new ActionRefusal(
                "shop.cannot_afford",
                $"That costs {Shop.Price(offer.CostCopper)} and the purse holds {Shop.Price(GoldCopper)}.");
        }

        if (offer.Potion is { } potency)
        {
            GoldCopper -= offer.CostCopper;
            _states[offer.MemberIndex] = _states[offer.MemberIndex].Carrying(potency);
            _lootFound.Add(offer.Description.Replace(" — ", ", bought for "));
            return null;
        }

        if (offer.NewDraft is not { } draft)
        {
            return new ActionRefusal("shop.empty_offer", "That offer carries nothing to buy.");
        }

        var party = Party.ToArray();
        var before = party[offer.MemberIndex].Sheet.MaximumHitPoints;

        party[offer.MemberIndex] = PregeneratedParty.Resolve(
            _content,
            draft,
            _states[offer.MemberIndex].Level,
            x: 0,
            y: offer.MemberIndex);

        // Bought gear can raise the hit point maximum the way found gear can; the
        // extra arrives as extra hit points, not as healing, exactly like a level's.
        var gained = Math.Max(0, party[offer.MemberIndex].Sheet.MaximumHitPoints - before);

        if (gained > 0)
        {
            _states[offer.MemberIndex] = _states[offer.MemberIndex] with
            {
                CurrentHitPoints = _states[offer.MemberIndex].CurrentHitPoints + gained,
            };
        }

        GoldCopper -= offer.CostCopper;
        Party = party;
        _lootFound.Add(offer.Description.Replace(" — ", ", bought for "));

        return null;
    }

    /// <summary>
    /// Awards the fight's experience and levels up anyone who has earned it.
    /// </summary>
    /// <remarks>
    /// Only the living earn, and the award is shared among them — so a party that has
    /// lost someone advances slightly faster per head, which is the arithmetic being
    /// honest rather than a consolation.
    /// </remarks>
    private void AwardExperience(Fight fight)
    {
        var earners = _states.Count(state => state.CanFight);

        if (earners == 0)
        {
            return;
        }

        var award = ExperienceRules.AwardPerCharacter(fight.Built.Monsters, earners);
        var party = Party.ToArray();

        for (var i = 0; i < _states.Count; i++)
        {
            var before = _states[i];
            var after = before.Earning(award);
            _states[i] = after;

            if (after.Level == before.Level)
            {
                continue;
            }

            // Levelling is re-resolving the draft at the new level. Nothing on a sheet is
            // edited, so a levelled character cannot hold a number that disagrees with
            // the rules that made it.
            var previousMaximum = party[i].Sheet.MaximumHitPoints;

            // The default only ever applies to a run that started from created drafts
            // or a resumed save (see _defaultsPlanlessAsi) — never to the plain
            // pregenerated party, whose own drafts carry the identical gap (#330) but
            // are what PacingMeasure and the baseline gameplay tests measure.
            string? asiDefaultNotice;

            if (_defaultsPlanlessAsi)
            {
                (party[i], asiDefaultNotice) = ResolveMember(_content, party[i].Draft, after.Level);
            }
            else
            {
                party[i] = PregeneratedParty.Resolve(_content, party[i].Draft, after.Level);
                asiDefaultNotice = null;
            }

            // The new level's extra hit points arrive as extra hit points, not as
            // healing: damage already taken stays taken, which is what the SRD's
            // "your Hit Point maximum increases" says and no more.
            var gained = Math.Max(0, party[i].Sheet.MaximumHitPoints - previousMaximum);

            _states[i] = after with
            {
                CurrentHitPoints = after.CurrentHitPoints + gained,
                HitDiceRemaining = after.HitDiceRemaining + (after.Level - before.Level),
            };

            _levelUps.Add($"{party[i].Draft.Name} reaches level {after.Level}");

            if (asiDefaultNotice is not null)
            {
                _levelUps.Add(asiDefaultNotice);
            }
        }

        Party = party;
    }

    /// <summary>
    /// Resolves a draft at a level, and — the honest fallback for a save written before
    /// creation flows asked for one — defaults a never-chosen Ability Score Improvement
    /// to +2 the class's primary ability rather than silently forfeiting it.
    /// </summary>
    /// <remarks>
    /// <b>The smaller honest option, stated:</b> prompting mid-run for a choice creation
    /// never asked is disproportionate — there is no interactive moment between fights
    /// for it, on either client — so the default lands instead, and the default is never
    /// silent: it is a returned notice, which every caller folds into
    /// <see cref="LevelUps"/> so a client narrates it exactly like the level-up itself.
    /// Only a draft with <em>no</em> plan at all is defaulted; a draft naming a real
    /// choice, however it arrived, is always the resolver's to apply.
    /// </remarks>
    private static (PartyMember Member, string? AsiDefaultNotice) ResolveMember(
        SrdContent content, CharacterDraft draft, int level, int x = 0, int y = 0)
    {
        var resolved = PregeneratedParty.Resolve(content, draft, level, x, y);

        if (draft.AbilityScoreImprovements.Count > 0 || resolved.Sheet.UnspentFeatChoices == 0)
        {
            return (resolved, null);
        }

        var primary = content.ClassesById[draft.ClassId].PrimaryAbilities[0];
        var defaulted = draft with { AbilityScoreImprovements = [new AbilityScoreImprovement { First = primary }] };
        resolved = PregeneratedParty.Resolve(content, defaulted, level, x, y);

        return (
            resolved,
            $"{draft.Name}'s Ability Score Improvement was never chosen — defaulted to +2 {primary}");
    }

    /// <summary>Level-ups in the order they happened, for a client to narrate.</summary>
    public IReadOnlyList<string> LevelUps => _levelUps;

    /// <summary>Fallen characters rejoining the party, in the order they came back.</summary>
    public IReadOnlyList<string> Returns => _returns;

    /// <summary>Loot found, in the order it dropped, for a client to narrate.</summary>
    public IReadOnlyList<string> LootFound => _lootFound;

    /// <summary>Characters who are dead right now, as opposed to who has ever fallen.</summary>
    /// <remarks>
    /// <see cref="Casualties"/> is the history and this is the state; they differ once a
    /// fallen character can come back, and a run's ending should report the latter.
    /// </remarks>
    public IEnumerable<string> Fallen => Party
        .Where((_, index) => _states[index].IsDead)
        .Select(member => member.Draft.Name);
}
