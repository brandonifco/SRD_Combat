namespace SRDCombat.Core.Dice;

/// <summary>
/// The only source of randomness the rules engine is allowed to use.
/// </summary>
/// <remarks>
/// Every roll goes through this so a fight can be replayed exactly. That is what makes
/// the frozen-transcript tests possible — they pin the precise narrated sequence of a
/// scripted fight and would be worthless against an engine that reached for
/// <see cref="System.Random.Shared"/> anywhere inside itself.
/// </remarks>
public interface IRandomSource
{
    /// <summary>Rolls one die, returning a value from 1 to <paramref name="sides"/> inclusive.</summary>
    int Roll(int sides);
}

/// <summary>A reproducible source: the same seed always produces the same fight.</summary>
public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);

    /// <summary>The seed this source was created with, so a fight can record how to replay itself.</summary>
    public int Seed { get; } = seed;

    public int Roll(int sides)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);

        return _random.Next(1, sides + 1);
    }
}

/// <summary>
/// Returns a scripted sequence of die results, for tests that need a specific outcome
/// rather than a specific seed.
/// </summary>
/// <remarks>
/// Deliberately throws once the script runs out rather than falling back to real
/// randomness: a test that rolls more dice than it accounted for is a test whose
/// premise has changed, and silently continuing would hide that.
/// </remarks>
public sealed class ScriptedRandomSource(params int[] results) : IRandomSource
{
    private readonly int[] _results = results;
    private int _index;

    public int Roll(int sides)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);

        if (_index >= _results.Length)
        {
            throw new InvalidOperationException(
                $"The scripted die ran out after {_results.Length} roll(s); a d{sides} was requested.");
        }

        var result = _results[_index++];

        return result >= 1 && result <= sides
            ? result
            : throw new InvalidOperationException($"Scripted roll {result} is not a legal d{sides} result.");
    }
}
