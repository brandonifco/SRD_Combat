using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests;

public class DiceExpressionTests
{
    [Theory]
    [InlineData("2d6 + 3", 2, 6, 3)]
    [InlineData("2d6+3", 2, 6, 3)]
    [InlineData("1d12", 1, 12, 0)]
    [InlineData("17d12 + 85", 17, 12, 85)]
    [InlineData("2d6 - 1", 2, 6, -1)]
    [InlineData("1", 0, 0, 1)]
    public void Parse_ReadsTheFormsTheSrdPrints(string text, int count, int sides, int modifier)
    {
        var expression = DiceExpression.Parse(text);

        Assert.Equal(count, expression.Count);
        Assert.Equal(sides, expression.Sides);
        Assert.Equal(modifier, expression.Modifier);
    }

    [Fact]
    public void Parse_AcceptsTheUnicodeMinusTheSrdUses()
    {
        // The PDF sets negative values with U+2212, not an ASCII hyphen. Without this,
        // every negative modifier in the book fails to parse.
        var expression = DiceExpression.Parse("2d6 − 1");

        Assert.Equal(-1, expression.Modifier);
    }

    [Theory]
    // Every one of these is a printed SRD value: the book states the average alongside
    // the dice, and the average is the expression rounded down.
    [InlineData("2d8 + 2", 11)]
    [InlineData("8d8 + 16", 52)]
    [InlineData("17d12 + 85", 195)]
    [InlineData("3d6", 10)]
    [InlineData("1d10 + 3", 8)]
    [InlineData("5d6", 17)]
    [InlineData("3d6 + 8", 18)]
    [InlineData("4d10", 22)]
    public void Average_MatchesThePrintedValue(string text, int expected) =>
        Assert.Equal(expected, DiceExpression.Parse(text).Average);

    [Theory]
    [InlineData("2d6 + 3")]
    [InlineData("1d12")]
    [InlineData("2d6 - 1")]
    [InlineData("1")]
    public void ToString_RoundTrips(string text) =>
        Assert.Equal(text, DiceExpression.Parse(text).ToString());

    [Fact]
    public void MinimumAndMaximum_BoundTheRoll()
    {
        var expression = DiceExpression.Parse("2d6 + 3");

        Assert.Equal(5, expression.Minimum);
        Assert.Equal(15, expression.Maximum);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not dice")]
    [InlineData("d6")]
    public void TryParse_RejectsWhatIsNotADiceExpression(string? text) =>
        Assert.False(DiceExpression.TryParse(text, out _));
}
