namespace SRDCombat.Core.Dice;

/// <summary>How a d20 is rolled.</summary>
public enum RollMode
{
    Normal,
    Advantage,
    Disadvantage,
}

/// <summary>
/// The outcome of a d20 roll, keeping both dice when two were rolled so narration can
/// show the player what actually happened.
/// </summary>
/// <param name="Rolls">Every d20 rolled, in the order rolled.</param>
/// <param name="Natural">The die result actually used, before modifiers.</param>
/// <param name="Modifier">The total modifier added.</param>
/// <param name="Mode">How it was rolled.</param>
public sealed record D20Roll(IReadOnlyList<int> Rolls, int Natural, int Modifier, RollMode Mode)
{
    /// <summary>The final result.</summary>
    public int Total => Natural + Modifier;

    /// <summary>True on a natural 20.</summary>
    public bool IsNatural20 => Natural == 20;

    /// <summary>True on a natural 1.</summary>
    public bool IsNatural1 => Natural == 1;

    public override string ToString()
    {
        var dice = Mode == RollMode.Normal
            ? Natural.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{string.Join('/', Rolls)} -> {Natural}";

        return $"d20 {dice}{Modifier:+0;-0;+0} = {Total}";
    }
}

/// <summary>Rolling a d20 against the SRD's Advantage/Disadvantage rules.</summary>
public static class D20Test
{
    /// <summary>
    /// Combines every source of Advantage and Disadvantage into one mode.
    /// </summary>
    /// <remarks>
    /// The SRD is explicit that these do not stack and that they cancel: any amount of
    /// Advantage plus any amount of Disadvantage means the roll is made normally, no
    /// matter how many of each apply.
    /// </remarks>
    public static RollMode Combine(bool hasAdvantage, bool hasDisadvantage) =>
        (hasAdvantage, hasDisadvantage) switch
        {
            (true, false) => RollMode.Advantage,
            (false, true) => RollMode.Disadvantage,
            _ => RollMode.Normal,
        };

    /// <summary>Combines an existing mode with further Advantage or Disadvantage.</summary>
    public static RollMode Combine(RollMode mode, bool hasAdvantage, bool hasDisadvantage) =>
        Combine(
            mode == RollMode.Advantage || hasAdvantage,
            mode == RollMode.Disadvantage || hasDisadvantage);

    /// <summary>Rolls a d20 with the given modifier and mode.</summary>
    public static D20Roll Roll(IRandomSource random, int modifier, RollMode mode = RollMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (mode == RollMode.Normal)
        {
            var single = random.Roll(20);
            return new D20Roll([single], single, modifier, mode);
        }

        var first = random.Roll(20);
        var second = random.Roll(20);

        var natural = mode == RollMode.Advantage
            ? Math.Max(first, second)
            : Math.Min(first, second);

        return new D20Roll([first, second], natural, modifier, mode);
    }
}
