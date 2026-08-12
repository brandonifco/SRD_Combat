using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The two printed range bands that carry no number: "Touch" and "Self".
/// </summary>
/// <remarks>
/// Both parse to no distance, and a null range means "no limit" wherever it is checked —
/// so Inflict Wounds, a Touch spell, was castable across the room until
/// <c>TargetRangeFeet</c> existed.
/// </remarks>
public class SpellRangeTests
{
    [Fact]
    public void TouchIsFiveFeet()
    {
        var touch = Spell("Touch");

        Assert.Equal(5, touch.TargetRangeFeet);
        Assert.False(touch.IsSelfRanged);
    }

    [Fact]
    public void SelfHasNoTargetRangeAndSaysSo()
    {
        // A Self spell is not aimed at anybody: its area is centred on the caster, so
        // there is no target distance to check. The flag is how a caller tells that
        // apart from a genuinely unlimited range.
        var self = Spell("Self");

        Assert.True(self.IsSelfRanged);
        Assert.Null(self.TargetRangeFeet);

        Assert.True(Spell("Self (15-foot Emanation)").IsSelfRanged);
    }

    [Fact]
    public void APrintedDistanceIsLeftAlone()
    {
        var ranged = Spell("120 feet", rangeFeet: 120);

        Assert.Equal(120, ranged.TargetRangeFeet);
        Assert.False(ranged.IsSelfRanged);
    }

    private static SpellDefinition Spell(string rangeText, int? rangeFeet = null) => new()
    {
        Id = "spell.test",
        Name = "Test",
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Cleric"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.SavingThrow,
        SourcePage = 1,
        RangeText = rangeText,
        RangeFeet = rangeFeet,
        Text = "A test spell.",
    };
}
