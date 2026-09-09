using SRDCombat.Core.Definitions;
using SRDCombat.Game;

namespace SRDCombat.Console.Tests;

/// <summary>
/// <see cref="PartyCreator.DescribeSpellPrompt"/> — the text printed right before
/// "Take {spell.Name}? (y/n)" is asked. #706: Spirit Guardians is allowlisted with one
/// named approximation (<see cref="PreparableSpells.Approximation"/>), and this is where
/// the console half of that promise is kept — extracted as a plain-value seam, the way
/// #490a/#490b pulled the Godot argv decision out of a live node, since
/// <c>ChooseSpells</c> itself reads from <c>System.Console</c> and cannot be called from
/// a test.
/// </summary>
public class PartyCreatorSpellPromptTests
{
    [Fact]
    public void SpiritGuardiansPromptCarriesTheFullApproximationCaveat()
    {
        // Asserted against PreparableSpells.Approximation itself, not a marker
        // substring — a helper that appended only the word "Approximated" and dropped
        // the explanation (one-shot strike, no repeat saves, unchanged Speed) would
        // pass a bare Contains("Approximated") check while breaking the acceptance
        // criteria's actual promise.
        var approximation = PreparableSpells.Approximation("spell.spirit-guardians");
        Assert.NotNull(approximation);

        var text = PartyCreator.DescribeSpellPrompt(Spell("spell.spirit-guardians", "Spirit Guardians"));

        Assert.Contains(approximation, text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherAllowlistedSpellsPromptCarriesNoCaveat()
    {
        Assert.Null(PreparableSpells.Approximation("spell.fireball"));

        var text = PartyCreator.DescribeSpellPrompt(Spell("spell.fireball", "Fireball"));
        var spiritGuardiansCaveat = PreparableSpells.Approximation("spell.spirit-guardians")!;

        Assert.DoesNotContain("Approximated", text, StringComparison.Ordinal);
        Assert.DoesNotContain(spiritGuardiansCaveat, text, StringComparison.Ordinal);
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
