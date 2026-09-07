using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Pins the class-feature guard preamble (#638, <c>Encounter.TryGetCombatantWithFeature</c>)
/// at every one of the nine class-feature action sites that route through it, so the two
/// refusals it hands back — <c>encounter.complete</c> when there is no combatant whose turn
/// it is, and <c>feature.absent</c> when the active combatant lacks the named feature —
/// cannot silently drift or stop firing at a call site.
/// </summary>
/// <remarks>
/// The sibling of <see cref="GuardPreambleTests"/>, which pins #320's universal preamble.
/// Before this class the feature family's <c>encounter.complete</c> leg was pinned by
/// nothing and its <c>feature.absent</c> leg by only four sites (Rage, SecondWind,
/// ActionSurge, CunningAction). These two tests spread both legs across all nine converted
/// sites so a regression in the one helper is caught wherever it is called — the knockout
/// table in the #638 PR body is built from them.
/// </remarks>
public class FeatureGuardPreambleTests
{
    private static Battlefield Field() => new(12, 12);

    /// <summary>Every class-feature action, invoked with arguments that are otherwise valid,
    /// so the only refusal in play is the preamble's own.</summary>
    private static IEnumerable<(string Site, ActionRefusal? Refusal)> FeatureActions(
        Encounter encounter,
        Combatant target) =>
        [
            ("Rage", encounter.Rage()),
            ("SecondWind", encounter.SecondWind()),
            ("SteadyAim", encounter.SteadyAim()),
            ("ActionSurge", encounter.ActionSurge()),
            ("RecklessAttack", encounter.RecklessAttack()),
            ("CunningStrike", encounter.CunningStrike(CunningStrikeEffect.Trip)),
            ("DivineSpark", encounter.DivineSpark(target, DivineSparkUse.Heal)),
            ("TurnUndead", encounter.TurnUndead([target])),
            ("CunningAction", encounter.CunningAction(CunningActionKind.Dash)),
        ];

    [Fact]
    public void EveryFeatureActionRefusesWithEncounterCompleteOnceTheFightIsOver()
    {
        var hero = CombatTestData.Combatant("hero", x: 0, stats: CombatTestData.Stats(initiativeBonus: 10));
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(maximumHitPoints: 1, armorClass: 1));

        // Three initiative rolls, then a natural 20 (auto-hit, auto-crit) and its two
        // damage dice: the hero drops the lone monster and its side is the last standing.
        var encounter = Encounter.Start(Field(), [hero, monster], new SeededSequence(10, 1, 20, 4, 4));

        Assert.Null(encounter.Attack("Sword", monster));
        Assert.True(encounter.IsComplete);
        Assert.Null(encounter.ActiveCombatant);

        // The present-first null check fires before the feature check, so a featureless
        // combatant still yields encounter.complete at every site.
        AssertEveryFeatureSiteRefusesWith("encounter.complete", FeatureActions(encounter, monster));
    }

    [Fact]
    public void EveryFeatureActionRefusesWithFeatureAbsentWhenTheActiveCombatantLacksIt()
    {
        var hero = CombatTestData.Combatant("hero", x: 0, stats: CombatTestData.Stats(initiativeBonus: 10));
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(initiativeBonus: 0));

        var encounter = Encounter.Start(Field(), [hero, monster], new SeededSequence(10, 10));

        // The hero is the active combatant, able to act, but carries no class features
        // (its Stats.Character is null), so every feature action refuses feature.absent.
        Assert.Same(hero, encounter.ActiveCombatant);
        Assert.True(hero.CanAct);

        AssertEveryFeatureSiteRefusesWith("feature.absent", FeatureActions(encounter, monster));
    }

    /// <summary>
    /// Evaluates all feature sites and asserts every one refused with the expected code,
    /// reporting each deviation by name so a knockout of either guard leg shows the whole
    /// per-site table rather than stopping at the first mismatch.
    /// </summary>
    private static void AssertEveryFeatureSiteRefusesWith(
        string expected,
        IEnumerable<(string Site, ActionRefusal? Refusal)> results)
    {
        var deviations = results
            .Where(result => result.Refusal?.Code != expected)
            .Select(result => $"{result.Site}={result.Refusal?.Code ?? "null"}")
            .ToList();

        Assert.True(
            deviations.Count == 0,
            $"sites not refusing {expected}: {string.Join(", ", deviations)}");
    }
}
