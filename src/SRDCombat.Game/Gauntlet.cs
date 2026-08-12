using SRDCombat.Content;
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
public sealed record LadderStep(EncounterDifficulty Difficulty, RestKind? RestBefore = null);

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
/// The default shape is a stated design choice rather than anything the SRD prints: each
/// level gets three fights, rising Low → Moderate → High, with a Short Rest between them
/// and a Long Rest before each new level. That gives the short-rest resources — a
/// Barbarian's Rage, a Fighter's Second Wind — something to be scarce *for*, which a
/// ladder of long rests would quietly remove.
/// </para>
/// </remarks>
public static class GauntletLadder
{
    /// <summary>Fights in one cycle of rising difficulty.</summary>
    public const int FightsPerCycle = 3;

    /// <summary>
    /// The default ladder: cycles of Low, Moderate, High until the run is long enough to
    /// carry a party from level 1 to level 5.
    /// </summary>
    /// <remarks>
    /// The length is chosen against the arithmetic rather than picked: a cycle awards
    /// each character the low, moderate and high per-character budgets added together,
    /// and reaching level 5 costs 6,500 XP, so a run needs roughly thirty fights. Ending
    /// a cycle on High and resting long afterwards gives the short-rest resources
    /// something to be scarce for within a cycle.
    /// </remarks>
    public static IReadOnlyList<LadderStep> Default(int fights = 30)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fights, 1);

        return Enumerable.Range(0, fights)
            .Select(index => new LadderStep(
                (index % FightsPerCycle) switch
                {
                    0 => EncounterDifficulty.Low,
                    1 => EncounterDifficulty.Moderate,
                    _ => EncounterDifficulty.High,
                },
                // No rest before the first fight; a Long Rest at the start of each new
                // cycle, a Short Rest between fights within one.
                index == 0 ? null : index % FightsPerCycle == 0 ? RestKind.Long : RestKind.Short))
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

    private GauntletRun(
        SrdContent content,
        IReadOnlyList<LadderStep> ladder,
        IReadOnlyList<PartyMember> party,
        IReadOnlyList<CharacterState> states)
    {
        _content = content;
        _states = [.. states];
        Ladder = ladder;
        Party = party;
    }

    /// <summary>Starts a run with a fresh party.</summary>
    public static GauntletRun Start(
        SrdContent content,
        IReadOnlyList<LadderStep>? ladder = null,
        int startingLevel = 1)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rungs = ladder ?? GauntletLadder.Default();

        if (rungs.Count == 0)
        {
            throw new ArgumentException("A run needs at least one rung.", nameof(ladder));
        }

        var party = PregeneratedParty.Build(content, startingLevel);

        return new GauntletRun(content, rungs, party, [.. party.Select(CharacterState.Fresh)]);
    }

    /// <summary>The rungs, in order.</summary>
    public IReadOnlyList<LadderStep> Ladder { get; }

    /// <summary>The party, resolved at the level of the rung they are on.</summary>
    public IReadOnlyList<PartyMember> Party { get; private set; }

    /// <summary>What each member is carrying, in the same order as <see cref="Party"/>.</summary>
    public IReadOnlyList<CharacterState> States => _states;

    /// <summary>How many rungs have been cleared.</summary>
    public int Cleared { get; private set; }

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
                pair.state.SpellSlotsRemaining)))
            .ToArray();

        return EncounterFactory.Build(_content, survivors, step.Difficulty, random);
    }

    /// <summary>
    /// Records a finished fight: reads the survivors' state back and advances the ladder.
    /// </summary>
    public void CompleteFight(Fight fight)
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
        Cleared++;

        if (Cleared >= Ladder.Count)
        {
            Outcome = RunOutcome.Survived;
        }
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
            party[i] = PregeneratedParty.Resolve(_content, party[i].Draft, after.Level);

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
        }

        Party = party;
    }

    /// <summary>Level-ups in the order they happened, for a client to narrate.</summary>
    public IReadOnlyList<string> LevelUps => _levelUps;

    /// <summary>Fallen characters rejoining the party, in the order they came back.</summary>
    public IReadOnlyList<string> Returns => _returns;

    /// <summary>Characters who are dead right now, as opposed to who has ever fallen.</summary>
    /// <remarks>
    /// <see cref="Casualties"/> is the history and this is the state; they differ once a
    /// fallen character can come back, and a run's ending should report the latter.
    /// </remarks>
    public IEnumerable<string> Fallen => Party
        .Where((_, index) => _states[index].IsDead)
        .Select(member => member.Draft.Name);
}
