using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

public class DiceAndD20Tests
{
    [Fact]
    public void SeededRandomSource_ReplaysExactly()
    {
        // The whole frozen-transcript approach rests on this.
        var first = Enumerable.Range(0, 40).Select(_ => new SeededRandomSource(1234).Roll(20)).Distinct();

        Assert.Single(first);
    }

    [Fact]
    public void ScriptedRandomSource_ThrowsWhenItRunsOut()
    {
        var die = new ScriptedRandomSource(20);

        Assert.Equal(20, die.Roll(20));

        // Deliberately loud: a test that rolls more dice than it scripted has changed
        // its own premise, and falling back to real randomness would hide that.
        Assert.Throws<InvalidOperationException>(() => die.Roll(20));
    }

    [Theory]
    [InlineData(false, false, RollMode.Normal)]
    [InlineData(true, false, RollMode.Advantage)]
    [InlineData(false, true, RollMode.Disadvantage)]
    // The rule that catches people out: they cancel exactly, no matter how many of each.
    [InlineData(true, true, RollMode.Normal)]
    public void Combine_CancelsAdvantageAgainstDisadvantage(bool advantage, bool disadvantage, RollMode expected) =>
        Assert.Equal(expected, D20Test.Combine(advantage, disadvantage));

    [Fact]
    public void Roll_WithAdvantage_KeepsTheHigherDie()
    {
        var roll = D20Test.Roll(new ScriptedRandomSource(7, 15), modifier: 3, RollMode.Advantage);

        Assert.Equal([7, 15], roll.Rolls);
        Assert.Equal(15, roll.Natural);
        Assert.Equal(18, roll.Total);
    }

    [Fact]
    public void Roll_WithDisadvantage_KeepsTheLowerDie()
    {
        var roll = D20Test.Roll(new ScriptedRandomSource(7, 15), modifier: 3, RollMode.Disadvantage);

        Assert.Equal(7, roll.Natural);
        Assert.Equal(10, roll.Total);
    }

    [Fact]
    public void Roll_Normally_RollsOneDie()
    {
        var roll = D20Test.Roll(new ScriptedRandomSource(11), modifier: -1);

        Assert.Single(roll.Rolls);
        Assert.Equal(10, roll.Total);
        Assert.False(roll.IsNatural20);
        Assert.False(roll.IsNatural1);
    }

    [Fact]
    public void DiceRoller_OnACriticalHit_DoublesTheDiceButNotTheModifier()
    {
        // 1d8 + 3 crits into 2d8 + 3. Doubling the total instead would be 22, not 14.
        var result = DiceRoller.Roll(
            new ScriptedRandomSource(4, 7),
            DiceExpression.Parse("1d8 + 3"),
            critical: true);

        Assert.Equal([4, 7], result.Dice);
        Assert.Equal(14, result.Total);
        Assert.True(result.WasCritical);
    }

    [Fact]
    public void DiceRoller_OnAFlatExpression_HasNothingToDouble()
    {
        // The Blowgun's damage, and a few weak monster attacks, are a flat 1.
        var result = DiceRoller.Roll(new ScriptedRandomSource(), DiceExpression.Flat(1), critical: true);

        Assert.Empty(result.Dice);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public void DiceRollResult_NeverGoesNegative()
    {
        var result = DiceRoller.Roll(new ScriptedRandomSource(1), DiceExpression.Parse("1d4 - 5"));

        Assert.Equal(0, result.Total);
    }
}
