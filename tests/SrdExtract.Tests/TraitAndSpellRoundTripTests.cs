using SRDCombat.Content;
using SRDCombat.Core.Definitions;
using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// The sibling to <see cref="CorpusRoundTripTests"/> that #382's design document calls
/// for in §8.2 (docs/2026-08-24-span-accounting-design.md). <c>CorpusRoundTripTests</c>
/// re-parses monsters only, so nothing in the suite would otherwise notice a stage-1 or
/// stage-2 behaviour change in the shared <c>ParseAppliedConditions</c> grammar as it
/// applies through <c>ClassifyTrait</c>'s other two callers — <c>OriginParser</c>
/// (species traits) and <c>ClassParser</c> (class features) share it directly, and
/// <c>SpellParser</c> shares it with <c>consultInertList: false</c>.
/// </summary>
/// <remarks>
/// <para>
/// Green from stage 0 through stage 3: none of those stages are meant to move a single
/// species, class or spell's classified conditions or grade, and this is what proves
/// it where <c>CorpusRoundTripTests</c> is not looking. Residue is deliberately
/// excluded from the comparison — that is what stage 4 is allowed to move.
/// </para>
/// <para>
/// Species traits and class features are compared directly: <c>OriginParser</c> and
/// <c>ClassParser</c> store exactly what <see cref="EntryMechanicsParser.ClassifyTrait"/>
/// returns, with no further transformation of <c>AppliedConditions</c> or
/// <c>Mechanics</c>. Spells are not — <c>SpellParser</c> layers its own attack-rider and
/// mechanics-fallthrough logic on top of <c>classified</c> (design §8.1), so the
/// comparison here follows that same logic rather than asserting a raw equality that
/// would already be false on today's <c>main</c>: <c>AppliedConditions</c> is compared
/// after replaying the attack-rider substitution
/// (<c>SpellEffectParser.ParseAttackRider</c>, gated on the spell's own stored
/// <c>IsSpellAttack</c>), and <c>Mechanics</c> is compared only in the one direction
/// the design's own argument rests on: a spell stored <c>Unmodelled</c> requires the
/// reparsed <c>classified.Mechanics</c> to be <c>Unmodelled</c> too.
/// </para>
/// </remarks>
public sealed class TraitAndSpellRoundTripTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    public static IEnumerable<object[]> SpeciesTraits() =>
        Content.Species.SelectMany(
            species => species.Traits,
            (species, trait) => new object[] { species.Name, trait });

    public static IEnumerable<object[]> ClassFeatures() =>
        Content.Classes.SelectMany(
            @class => @class.Features.Concat(@class.SubclassFeatures),
            (@class, feature) => new object[] { @class.Name, feature });

    public static IEnumerable<object[]> Spells() =>
        Content.Spells.Select(spell => new object[] { spell });

    [Theory]
    [MemberData(nameof(SpeciesTraits))]
    public void ReparsingAStoredSpeciesTraitReproducesItsConditionsAndGrade(string speciesName, TraitEntry stored)
    {
        var reparsed = EntryMechanicsParser.ClassifyTrait(stored.Name, stored.Text);

        AssertSameConditionsAndGrade(speciesName, stored, reparsed);
    }

    [Theory]
    [MemberData(nameof(ClassFeatures))]
    public void ReparsingAStoredClassFeatureReproducesItsConditionsAndGrade(string className, TraitEntry stored)
    {
        var reparsed = EntryMechanicsParser.ClassifyTrait(stored.Name, stored.Text);

        AssertSameConditionsAndGrade(className, stored, reparsed);
    }

    [Theory]
    [MemberData(nameof(Spells))]
    public void ReparsingAStoredSpellReproducesTheConditionsAndGradeSpellParserWouldDerive(SpellDefinition spell)
    {
        var classified = EntryMechanicsParser.ClassifyTrait(spell.Name, spell.Text, consultInertList: false);

        // SpellParser.cs:281,326 — an attack rider, when the spell's own stored
        // IsSpellAttack gate allows one, replaces classified.AppliedConditions
        // entirely rather than sitting alongside it.
        var attackRider = spell.IsSpellAttack ? SpellEffectParser.ParseAttackRider(spell.Text) : null;
        var expectedConditions = attackRider is not null
            ? (IReadOnlyList<AppliedCondition>)[attackRider]
            : classified.AppliedConditions;

        Assert.True(
            expectedConditions.SequenceEqual(spell.AppliedConditions),
            $"'{spell.Name}': reparsed conditions [{Describe(expectedConditions)}] did not match " +
            $"the stored conditions [{Describe(spell.AppliedConditions)}].");

        // SpellParser.cs — Mechanics falls through to classified.Mechanics only when
        // the spell has no save, no attack, no heal and no revival, which is exactly
        // when the stored grade is Unmodelled (§2.7, §8 of the design document). The
        // reverse is not asserted: a save/attack/heal spell's stored Mechanics is a
        // spell-level determination classified never makes on its own.
        if (spell.Mechanics == EntryMechanics.Unmodelled)
        {
            Assert.Equal(EntryMechanics.Unmodelled, classified.Mechanics);
        }
    }

    private static void AssertSameConditionsAndGrade(string ownerName, TraitEntry stored, TraitEntry reparsed)
    {
        Assert.True(
            reparsed.AppliedConditions.SequenceEqual(stored.AppliedConditions),
            $"'{ownerName}' :: '{stored.Name}': reparsed conditions [{Describe(reparsed.AppliedConditions)}] " +
            $"did not match the stored conditions [{Describe(stored.AppliedConditions)}].");

        Assert.Equal(stored.Mechanics, reparsed.Mechanics);
    }

    private static string Describe(IReadOnlyList<AppliedCondition> conditions) =>
        string.Join(", ", conditions.Select(condition => condition.Condition));
}
