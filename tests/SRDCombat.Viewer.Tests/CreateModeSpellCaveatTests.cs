using SRDCombat.Core.Definitions;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="CreateMode.DescribeSpell"/> — the point-of-choice text a player reads
/// while browsing the verified spell menu. #706: Spirit Guardians is allowlisted with
/// one named approximation (<see cref="SRDCombat.Game.PreparableSpells.Approximation"/>),
/// and this is where the client half of that promise is kept — the same rule
/// <c>DescribeSpecies</c> already follows for a species trait it doesn't claim.
/// </summary>
public sealed class CreateModeSpellCaveatTests
{
    [Fact]
    public void SpiritGuardiansDescriptionCarriesTheApproximationCaveat()
    {
        var text = CreateMode.DescribeSpell(Spell("spell.spirit-guardians", "Spirit Guardians"));

        Assert.Contains("Approximated", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherAllowlistedSpellsDescriptionCarriesNoCaveat()
    {
        var text = CreateMode.DescribeSpell(Spell("spell.fireball", "Fireball"));

        Assert.DoesNotContain("Approximated", text, StringComparison.Ordinal);
    }

    private static SpellDefinition Spell(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Level = 3,
        School = MagicSchool.Evocation,
        Classes = ["Wizard"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        RangeText = "150 feet",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Text = $"{name} — printed text stands in for the real spell here.",
        Mechanics = EntryMechanics.SavingThrow,
        SourcePage = 1,
    };
}
