using SRDCombat.Content;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game;

/// <summary>
/// Builds the fight a scenario describes, at a seed. The one entry point every consumer
/// of a <see cref="BattleScenario"/> uses (#474).
/// </summary>
/// <remarks>
/// <para>
/// <b>A scenario overrides draws; it never bypasses generation</b> — the single most
/// important constraint in the battle-builder design (§6), and the reason this class is
/// forty lines rather than four hundred. Every value a scenario authors is a value the
/// generator would otherwise have drawn, so the scenario supplies the value and the
/// generator's code path is otherwise untouched: a budgeted scenario goes through
/// <see cref="EncounterFactory.Build"/> and an explicit cast through
/// <see cref="EncounterFactory.BuildChosen"/>, both of which already share
/// <c>Assemble</c> — the layout draw, spawn fitting, terrain and combatant construction —
/// verbatim. There is no second board builder here, and there must never be one: a
/// scenario's fight stands on exactly the board a drawn fight would, or the numbers taken
/// from it are about a game that does not ship.
/// </para>
/// <para>
/// The proof obligation that keeps it honest, pinned by
/// <c>ScenarioRunnerTests.ABudgetedScenarioBuildsTheSameFightTheLadderWouldHave</c>: a
/// budgeted scenario naming the difficulty, level and CR cap the ladder would have used
/// produces the <b>identical</b> fight at the same seed — same cast, same board, same
/// initiative, same narration played out. If it ever diverges, a draw was skipped or
/// re-timed, which is a bug and not a tolerance to widen.
/// </para>
/// <para>
/// <b>The party is resolved, never built here.</b>
/// <see cref="ScenarioContent.ResolveParty"/> and
/// <see cref="ScenarioContent.ResolveRoster"/> are where a scenario's ids meet content;
/// what this class owns is the seed, the branch between the two questions a scenario can
/// ask, and handing the answer to the generator.
/// </para>
/// </remarks>
public static class ScenarioRunner
{
    /// <summary>
    /// Builds the scenario's fight at a seed, with initiative already rolled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The seed is the whole of the reproducibility promise.</b> The same
    /// <c>(scenario, seed)</c> produces the same fight — same cast, same board, same
    /// initiative order, same narration when played out — which is what makes a batch's
    /// <c>reproduce:</c> line a command rather than a hope, and the same promise
    /// <c>(seed, fight number)</c> already makes for a rung of the gauntlet
    /// (<see cref="RunDice"/>). Nothing here reads
    /// <see cref="BattleScenario.Seed"/>: that field is a bookmark a human follows, and
    /// quietly adopting it as a default would make a batch's numbers describe a different
    /// fight than the caller asked for.
    /// </para>
    /// <para>
    /// Refuses rather than approximates, in <see cref="ScenarioContent"/>'s voice. A
    /// scenario naming content this build does not have, a party that does not resolve,
    /// or a cast that cannot be deployed all fail by name and say which scenario they
    /// came from; <see cref="ScenarioContent.CheckAgainst"/> is the front door for a
    /// caller that wants every problem at once rather than the first.
    /// </para>
    /// </remarks>
    /// <param name="content">Loaded SRD content.</param>
    /// <param name="scenario">The authored fight.</param>
    /// <param name="seed">The dice this fight is rolled on.</param>
    public static Fight Build(SrdContent content, BattleScenario scenario, int seed)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scenario);

        var party = ScenarioContent.ResolveParty(scenario, content);
        var random = new SeededRandomSource(seed);

        // Resolution above, generation below, and the catch wraps only the second half.
        // ResolveParty refuses with an InvalidOperationException of its own when a class
        // table has no row at the level asked for, and that message is already about the
        // party rather than about deployment — dressing it as a placement failure would
        // be worse than not catching it at all.
        try
        {
            return scenario.Enemies.Budget is { } budget
                ? EncounterFactory.Build(content, party, budget, random, scenario.Objective)
                : EncounterFactory.BuildChosen(
                    party,
                    ScenarioContent.ResolveRoster(scenario, content),
                    random,
                    scenario.Objective);
        }
        catch (InvalidOperationException failure)
        {
            // SpawnPlacement.Fit's "bug report" throw, which names the creature and the
            // board it had no room on. Unreachable from any scenario the format admits
            // today — the board is sized from the cast it has to hold, and the largest
            // printed body is three squares on a twenty-eight-wide field — so this is
            // here to make sure that if the board ever does run out, the message says
            // which scenario asked for it rather than surfacing as a bare engine throw.
            throw new InvalidOperationException(
                $"scenario \"{scenario.Name}\" cannot be deployed: {failure.Message}", failure);
        }
    }
}
