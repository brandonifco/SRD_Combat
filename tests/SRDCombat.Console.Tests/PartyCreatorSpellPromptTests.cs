using SRDCombat.Core.Definitions;

namespace SRDCombat.Console.Tests;

/// <summary>
/// <see cref="PartyCreator.DescribeSpellPrompt"/> — the text printed right before
/// "Take {spell.Name}? (y/n)" is asked. #706: Spirit Guardians is allowlisted with one
/// named approximation (<see cref="SRDCombat.Game.PreparableSpells.Approximation"/>),
/// and this is where the console half of that promise is kept — extracted as a
/// plain-value seam, the way #490a/#490b pulled the Godot argv decision out of a live
/// node, since <c>ChooseSpells</c> itself reads from <c>System.Console</c> and cannot be
/// called from a test.
/// </summary>
public class PartyCreatorSpellPromptTests
{
    [Fact]
    public void SpiritGuardiansPromptCarriesTheApproximationCaveat()
    {
        var text = PartyCreator.DescribeSpellPrompt(Spell("spell.spirit-guardians", "Spirit Guardians"));

        Assert.Contains("Approximated", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherAllowlistedSpellsPromptCarriesNoCaveat()
    {
        var text = PartyCreator.DescribeSpellPrompt(Spell("spell.fireball", "Fireball"));

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
