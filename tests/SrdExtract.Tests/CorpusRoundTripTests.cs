using SRDCombat.Content;
using SRDCombat.Core.Definitions;
using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// Re-runs <see cref="EntryMechanicsParser.Classify"/> over every entry of every
/// monster in the committed corpus and asserts the result matches what is on disk.
/// This is the whole-corpus safety net #189 asks for: no PDF is needed, because the
/// corpus's own stored (name, section, text) is enough to reproduce what the parser
/// did — the corpus <em>is</em> the fixture set.
/// </summary>
/// <remarks>
/// The stored entry <c>name</c> is already bare (the usage suffix, e.g. "(Recharge
/// 6)", was parsed out of the printed heading and is preserved separately on
/// <c>usage</c>) — re-classifying from the bare name would silently lose it, so
/// <see cref="UsageLimit"/> is restored from the stored value before comparing.
/// </remarks>
public sealed class CorpusRoundTripTests
{
    private static readonly IReadOnlyList<MonsterDefinition> Monsters =
        ContentLoader.Load(RepositoryPaths.SrdContentDirectory).Monsters;

    public static IEnumerable<object[]> MonsterEntryPairs() =>
        Monsters.SelectMany(
            monster => monster.Entries,
            (monster, entry) => new object[] { monster.Name, entry });

    [Theory]
    [MemberData(nameof(MonsterEntryPairs))]
    public void ReparsingAStoredEntryReproducesItByteForByte(string monsterName, MonsterEntry stored)
    {
        var reparsed = EntryMechanicsParser.Classify(stored.Name, stored.Section, stored.Text)
            with
            { Usage = stored.Usage };

        var expected = ContentSerializer.Serialize(stored);
        var actual = ContentSerializer.Serialize(reparsed);

        Assert.True(
            expected == actual,
            $"Re-parsing '{monsterName}' :: '{stored.Name}' ({stored.Section}) did not reproduce " +
            $"the stored entry.\nExpected: {expected}\nActual:   {actual}");
    }
}
