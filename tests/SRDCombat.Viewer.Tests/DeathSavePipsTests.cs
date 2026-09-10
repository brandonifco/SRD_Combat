using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// A downed character's Death Save readout (#299): what <see cref="DeathSavePips"/>
/// draws for a given count, and that a real fight's scripted rolls reach the client
/// through <c>FightScreen.TokenFrom</c> unchanged.
/// </summary>
public class DeathSavePipsTests
{
    // ---- The pure seam ----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SuccessFilled_MatchesTheSuccessCount(int successes)
    {
        var pips = DeathSavePips.For(successes, failures: 0, isStable: false);

        for (var i = 0; i < DeathSavePips.PipsPerRow; i++)
        {
            Assert.Equal(i < successes, pips.SuccessFilled(i));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailureFilled_MatchesTheFailureCount(int failures)
    {
        var pips = DeathSavePips.For(0, failures, isStable: false);

        for (var i = 0; i < DeathSavePips.PipsPerRow; i++)
        {
            Assert.Equal(i < failures, pips.FailureFilled(i));
        }
    }

    [Fact]
    public void For_ClampsToThreePerRow()
    {
        // Combatant.AddDeathSaveSuccess/Failure already clamp at 3 (the SRD's own count,
        // p. 17), but the seam does not trust the caller to have done that — a value
        // above 3 must never draw a fourth pip.
        var pips = DeathSavePips.For(successes: 5, failures: 9, isStable: false);

        Assert.False(pips.SuccessFilled(3));
        Assert.False(pips.FailureFilled(3));
        Assert.True(pips.SuccessFilled(2));
        Assert.True(pips.FailureFilled(2));
    }

    /// <summary>
    /// Knockout for the Stable guard: dropping the <c>!IsStable &amp;&amp;</c> half of
    /// either filled check would make a Stable creature's (zeroed, per print) counts
    /// draw as "two failures, one from death" instead of "no longer rolling" — this
    /// passes non-zero counts alongside <c>isStable: true</c> precisely so a
    /// implementation that forgot the guard, and only ever saw <c>MarkStable</c>'s own
    /// zeroed counts in practice, still fails it.
    /// </summary>
    [Fact]
    public void Stable_DrawsNoPipsRegardlessOfWhateverCountsItIsGiven()
    {
        var pips = DeathSavePips.For(successes: 2, failures: 2, isStable: true);

        for (var i = 0; i < DeathSavePips.PipsPerRow; i++)
        {
            Assert.False(pips.SuccessFilled(i));
            Assert.False(pips.FailureFilled(i));
        }
    }

    [Theory]
    [InlineData(0, 0, "0/3 successes, 0/3 failures")]
    [InlineData(2, 1, "2/3 successes, 1/3 failures")]
    [InlineData(3, 0, "3/3 successes, 0/3 failures")]
    [InlineData(0, 2, "0/3 successes, 2/3 failures")]
    public void SummaryText_ReadsTheCounts(int successes, int failures, string expected) =>
        Assert.Equal(expected, DeathSavePips.For(successes, failures, isStable: false).SummaryText());

    [Fact]
    public void SummaryText_IsStableRegardlessOfCounts() =>
        Assert.Equal("stable", DeathSavePips.For(2, 1, isStable: true).SummaryText());

    // ---- Through a real fight: scripted Death Saves, read off the engine's own fields ----

    /// <summary>
    /// A downed combatant fails once, succeeds twice, then a third success stabilises it
    /// — <see cref="DeathSaveRules.Roll"/> is the same call <c>Encounter</c> makes, so
    /// this is the engine's own arithmetic and <c>DamageRules.MarkStable</c>'s reset, not
    /// a count this test invents.
    /// </summary>
    [Fact]
    public void DownedCombatant_CarriesItsScriptedDeathSavesOntoTheToken()
    {
        var combatant = FightTestData.Combatant(
            "Downed Fighter",
            stats: FightTestData.Stats(maximumHitPoints: 20, diesAtZeroHitPoints: false));

        DamageRules.Apply(combatant, 20, DamageType.Slashing);
        Assert.True(combatant.IsDying);

        var rolls = new ScriptedD20(3, 15, 15); // a failure, then two successes.
        DeathSaveRules.Roll(rolls, combatant);
        DeathSaveRules.Roll(rolls, combatant);
        DeathSaveRules.Roll(rolls, combatant);

        Assert.Equal(2, combatant.DeathSaveSuccesses);
        Assert.Equal(1, combatant.DeathSaveFailures);
        Assert.False(combatant.IsStable);

        var token = FightScreen.TokenFrom(combatant, Labels.For([combatant]));

        Assert.Equal(2, token.DeathSaveSuccesses);
        Assert.Equal(1, token.DeathSaveFailures);
        Assert.False(token.IsStable);

        var pips = DeathSavePips.For(token.DeathSaveSuccesses, token.DeathSaveFailures, token.IsStable);
        Assert.Equal("2/3 successes, 1/3 failures", pips.SummaryText());

        var row = FightScreen.RowFor(token, active: false, hidden: false);
        Assert.Equal("Downed — 2/3 successes, 1/3 failures", row.State);
    }

    [Fact]
    public void ThirdSuccess_StabilisesAndResetsTheCountsCombatantAndTokenAgree()
    {
        var combatant = FightTestData.Combatant(
            "Stabilising Fighter",
            stats: FightTestData.Stats(maximumHitPoints: 20, diesAtZeroHitPoints: false));

        DamageRules.Apply(combatant, 20, DamageType.Slashing);

        var rolls = new ScriptedD20(15, 15, 15);
        DeathSaveRules.Roll(rolls, combatant);
        DeathSaveRules.Roll(rolls, combatant);
        var third = DeathSaveRules.Roll(rolls, combatant);

        Assert.True(third.BecameStable);
        Assert.True(combatant.IsStable);
        // p. 17: "The number of both is reset to zero when you regain any Hit Points or
        // become Stable" — so the zero here is print's own consequence, not a gap.
        Assert.Equal(0, combatant.DeathSaveSuccesses);
        Assert.Equal(0, combatant.DeathSaveFailures);

        var token = FightScreen.TokenFrom(combatant, Labels.For([combatant]));

        Assert.True(token.IsStable);

        var row = FightScreen.RowFor(token, active: false, hidden: false);
        Assert.Equal("Downed — stable", row.State);
    }

    [Fact]
    public void ThirdFailure_KillsTheCombatantAndTheTokenReadsDeadNotDowned()
    {
        var combatant = FightTestData.Combatant(
            "Dying Fighter",
            stats: FightTestData.Stats(maximumHitPoints: 20, diesAtZeroHitPoints: false));

        DamageRules.Apply(combatant, 20, DamageType.Slashing);

        var rolls = new ScriptedD20(3, 3, 3);
        DeathSaveRules.Roll(rolls, combatant);
        DeathSaveRules.Roll(rolls, combatant);
        var third = DeathSaveRules.Roll(rolls, combatant);

        Assert.True(third.Died);
        Assert.True(combatant.IsDead);

        var token = FightScreen.TokenFrom(combatant, Labels.For([combatant]));

        Assert.True(token.IsDead);
        Assert.Equal(HealthBand.Dead, HealthReadout.BandFor(token.IsDead, token.IsDown, token.IsBloodied));

        var row = FightScreen.RowFor(token, active: false, hidden: false);
        Assert.Equal("dead", row.State);
    }

    // ---- Fog: nothing of this leaks for a hidden creature ----

    [Fact]
    public void HiddenDownedCombatant_ReadsUnseenNotItsDeathSaveProgress()
    {
        var combatant = FightTestData.Combatant(
            "Hidden Downed Enemy",
            stats: FightTestData.Stats(maximumHitPoints: 20, diesAtZeroHitPoints: false));

        DamageRules.Apply(combatant, 20, DamageType.Slashing);

        var rolls = new ScriptedD20(15, 3);
        DeathSaveRules.Roll(rolls, combatant); // one success
        DeathSaveRules.Roll(rolls, combatant); // one failure

        var token = FightScreen.TokenFrom(combatant, Labels.For([combatant]));
        Assert.True(token.IsDown);
        Assert.Equal(1, token.DeathSaveSuccesses);
        Assert.Equal(1, token.DeathSaveFailures);

        var row = FightScreen.RowFor(token, active: false, hidden: true);

        Assert.Equal("unseen", row.State);
        Assert.DoesNotContain("success", row.State, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure", row.State, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A scripted d20: each <see cref="Roll"/> call returns the next queued natural.</summary>
    private sealed class ScriptedD20(params int[] naturals) : IRandomSource
    {
        private int _index;

        public int Roll(int sides)
        {
            Assert.Equal(20, sides); // Death Saves are the only caller here.
            return naturals[_index++];
        }
    }
}
