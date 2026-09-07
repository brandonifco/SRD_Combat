using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Pins the shared guard preamble (#320, <c>Encounter.TryGetActingCombatant</c>) at every
/// universal-action site that routes through it, so the two refusals it hands back —
/// <c>encounter.complete</c> when there is no combatant whose turn it is, and
/// <c>combatant.cannot_act</c> when there is but it cannot act — cannot silently drift or
/// stop firing at a call site.
/// </summary>
/// <remarks>
/// Before this class the guard was barely pinned: knocking out its <c>CanAct</c> leg
/// turned exactly one existing test red (StandUp's), and knocking out its
/// <c>encounter.complete</c> leg turned none. These are deliberately spread across all
/// eight converted sites so a regression in the one helper is caught wherever it is
/// called — the knockout table in the #320 PR body is built from them.
/// </remarks>
public class GuardPreambleTests
{
    private static Battlefield Field() => new(12, 12);

    /// <summary>Every guarded action, invoked with arguments that are otherwise valid, so
    /// the only refusal in play is the preamble's own.</summary>
    private static IEnumerable<(string Site, ActionRefusal? Refusal)> GuardedActions(
        Encounter encounter,
        Combatant target,
        Combatant ally) =>
        [
            ("Move", encounter.Move(new GridPosition(2, 0))),
            ("Attack", encounter.Attack("Sword", target)),
            ("Dodge", encounter.Dodge()),
            ("Dash", encounter.Dash()),
            ("Disengage", encounter.Disengage()),
            ("StandUp", encounter.StandUp()),
            ("Escape", encounter.Escape()),
            ("UseEntry", encounter.UseEntry("Bite", target)),
            ("DrinkPotion", encounter.DrinkPotion(HealingPotion.Standard)),
            ("TradeItem", encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally)),
        ];

    [Fact]
    public void EveryGuardedActionRefusesWithEncounterCompleteOnceTheFightIsOver()
    {
        var hero = CombatTestData.Combatant("hero", x: 0, stats: CombatTestData.Stats(initiativeBonus: 10));
        var ally = CombatTestData.Combatant("ally", x: 6, stats: CombatTestData.Stats(initiativeBonus: 8));
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(maximumHitPoints: 1, armorClass: 1));

        // Three initiative rolls, then a natural 20 (auto-hit, auto-crit) and its two
        // damage dice: the hero drops the lone monster and its side is the last standing.
        var encounter = Encounter.Start(Field(), [hero, ally, monster], new SeededSequence(10, 10, 1, 20, 4, 4));

        Assert.Null(encounter.Attack("Sword", monster));
        Assert.True(encounter.IsComplete);
        Assert.Null(encounter.ActiveCombatant);

        AssertEveryGuardedSiteRefusesWith("encounter.complete", GuardedActions(encounter, monster, ally));
    }

    [Fact]
    public void EveryGuardedActionRefusesWithCannotActWhenTheActiveCombatantCannotAct()
    {
        var hero = CombatTestData.Combatant("hero", x: 0, stats: CombatTestData.Stats(initiativeBonus: 10));
        var ally = CombatTestData.Combatant("ally", x: 1, stats: CombatTestData.Stats(initiativeBonus: 5));
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 3,
            stats: CombatTestData.Stats(initiativeBonus: 0));

        var encounter = Encounter.Start(Field(), [hero, ally, monster], new SeededSequence(10, 10, 10));

        // The hero's turn has already begun; Paralyzed lands on it now, so it is the
        // active combatant and yet cannot act — the exact state the CanAct leg guards.
        hero.AddCondition(ConditionType.Paralyzed);
        Assert.Same(hero, encounter.ActiveCombatant);
        Assert.False(hero.CanAct);

        AssertEveryGuardedSiteRefusesWith("combatant.cannot_act", GuardedActions(encounter, monster, ally));
    }

    /// <summary>
    /// Evaluates all guarded sites and asserts every one refused with the expected code,
    /// reporting each deviation by name so a knockout of either guard leg shows the whole
    /// per-site table rather than stopping at the first mismatch.
    /// </summary>
    private static void AssertEveryGuardedSiteRefusesWith(
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
