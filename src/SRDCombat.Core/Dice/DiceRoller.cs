namespace SRDCombat.Core.Dice;

/// <summary>The outcome of rolling a dice expression.</summary>
/// <param name="Dice">Every die rolled, in order.</param>
/// <param name="Modifier">The flat modifier added once.</param>
/// <param name="WasCritical">Whether the dice were doubled for a Critical Hit.</param>
public sealed record DiceRollResult(IReadOnlyList<int> Dice, int Modifier, bool WasCritical)
{
    /// <summary>The total, never below zero — damage cannot heal.</summary>
    public int Total => Math.Max(0, Dice.Sum() + Modifier);

    public override string ToString() =>
        Dice.Count == 0
            ? Modifier.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"({string.Join('+', Dice)}){Modifier:+0;-0;+#} = {Total}";
}

/// <summary>Rolls dice expressions.</summary>
public static class DiceRoller
{
    /// <summary>Rolls an expression normally.</summary>
    public static DiceRollResult Roll(IRandomSource random, DiceExpression expression) =>
        Roll(random, expression, critical: false);

    /// <summary>
    /// Rolls an expression, optionally as a Critical Hit.
    /// </summary>
    /// <remarks>
    /// A Critical Hit rolls all of the attack's damage <em>dice</em> twice and adds the
    /// modifier once — not "double the total". For <c>1d6 + 3</c> that is 2d6 + 3, which
    /// averages 10, rather than 13.
    /// </remarks>
    public static DiceRollResult Roll(IRandomSource random, DiceExpression expression, bool critical)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(expression);

        var count = critical ? expression.Count * 2 : expression.Count;
        var dice = new int[count];

        for (var index = 0; index < count; index++)
        {
            dice[index] = random.Roll(expression.Sides);
        }

        // A flat expression has no dice at all; its value lives entirely in the
        // modifier and is deliberately not doubled by a Critical Hit, since there are no
        // dice to roll twice.
        return new DiceRollResult(dice, expression.Modifier, critical);
    }
}
