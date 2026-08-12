using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>One rung of the ladder: a fight at a level and a difficulty.</summary>
/// <param name="Level">The party level this rung is built for.</param>
/// <param name="Difficulty">How hard it should be.</param>
/// <param name="RestBefore">The rest the party gets before it, if any.</param>
public sealed record LadderStep(int Level, EncounterDifficulty Difficulty, RestKind? RestBefore = null);

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
    /// <summary>Fights at each level before the party moves up.</summary>
    public const int FightsPerLevel = 3;

    /// <summary>The default ladder: levels 1 to 5, three fights each.</summary>
    public static IReadOnlyList<LadderStep> Default(int fromLevel = 1, int toLevel = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fromLevel, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(toLevel, fromLevel);

        var steps = new List<LadderStep>();

        for (var level = fromLevel; level <= toLevel; level++)
        {
            for (var fight = 0; fight < FightsPerLevel; fight++)
            {
                var difficulty = fight switch
                {
                    0 => EncounterDifficulty.Low,
                    1 => EncounterDifficulty.Moderate,
                    _ => EncounterDifficulty.High,
                };

                // No rest before the very first fight of the run; a Long Rest on arriving
                // at a new level, a Short Rest between fights at the same one.
                var rest = (level, fight) switch
                {
                    _ when level == fromLevel && fight == 0 => (RestKind?)null,
                    (_, 0) => RestKind.Long,
                    _ => RestKind.Short,
                };

                steps.Add(new LadderStep(level, difficulty, rest));
            }
        }

        return steps;
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

    /// <summary>Starts a run with a fresh party at the ladder's first level.</summary>
    public static GauntletRun Start(SrdContent content, IReadOnlyList<LadderStep>? ladder = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rungs = ladder ?? GauntletLadder.Default();

        if (rungs.Count == 0)
        {
            throw new ArgumentException("A run needs at least one rung.", nameof(ladder));
        }

        var party = PregeneratedParty.Build(content, rungs[0].Level);

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

        // The party is re-resolved at the rung's level, which is what makes levelling a
        // matter of naming a higher level rather than editing anybody.
        if (step.Level != Party[0].Sheet.Level)
        {
            Party = PregeneratedParty.Build(_content, step.Level);
        }

        if (step.RestBefore is not { } rest)
        {
            return null;
        }

        for (var i = 0; i < _states.Count; i++)
        {
            _states[i] = _states[i].AfterRest(
                Party[i],
                rest,
                random,
                _content.ClassesById[Party[i].Draft.ClassId].HitDieSides);
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

        Cleared++;

        if (Cleared >= Ladder.Count)
        {
            Outcome = RunOutcome.Survived;
        }
    }
}
