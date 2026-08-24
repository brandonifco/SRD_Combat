using SRDCombat.Core.Definitions;
using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// Pins of CURRENT known-buggy behavior in <see cref="EntryMechanicsParser"/>. Each
/// test names the issue it documents and states plainly that it pins a bug, not a
/// spec — the span-coverage refactor (#382) is expected to make every one of these
/// assertions false, and that is the point: when it does, this file is the proof the
/// fix landed, and each test should be updated (not deleted silently) to assert the
/// corrected behavior.
/// </summary>
public sealed class KnownGapPinsTests
{
    [Fact]
    public void Issue370_FailureOrSuccessGoverningASideClauseCurrentlyOverridesTheWholeEntrysOutcome()
    {
        // Steam Mephit's Steam Breath, verbatim. "Failure or Success: Being underwater
        // doesn't grant Resistance to this Fire damage." is a side clause about
        // Resistance, not a restatement of the Failure damage — but MatchesStructuredForm
        // / ParseSave's success check only look for the label substring anywhere in the
        // text, so its presence currently overrides the printed "Success: Half damage
        // only." into SameAsFailure for the whole entry. Fix (#382/accounting halves of
        // #370): the side clause should be recognised as its own residue and the printed
        // Success: Half damage outcome preserved.
        var entry = EntryMechanicsParser.Classify(
            "Steam Breath",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 10, each creature in a 15-foot Cone. Failure: 5 (2d4) " +
            "Fire damage, and the target's Speed decreases by 10 feet until the end of the " +
            "mephit's next turn. Success: Half damage only. Failure or Success: Being " +
            "underwater doesn't grant Resistance to this Fire damage.");

        Assert.Equal(SaveSuccessOutcome.SameAsFailure, entry.Save!.SuccessOutcome);
    }

    [Fact]
    public void Issue371_AConditionalDamageAlternativeCurrentlyKeepsOnlyTheFirstComponent()
    {
        // Swarm of Rats' Bites, verbatim. "or 2 (1d4) Piercing damage if the swarm is
        // Bloodied" is a real, printed conditional alternative that the model has no
        // vocabulary for — but the attack's Hit: clause is credited whole by
        // MatchesStructuredForm, so the second damage figure is silently dropped rather
        // than counted. Fix: the uncovered "or 2 (1d4)..." span should land in residue.
        var entry = EntryMechanicsParser.Classify(
            "Bites",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +2, reach 5 ft. Hit: 5 (2d4) Piercing damage, or 2 (1d4) Piercing " +
            "damage if the swarm is Bloodied.");

        var damage = Assert.Single(entry.Attack!.Damage);
        Assert.Equal(5, damage.PrintedAverage);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void Issue372_APluralConditionsSentenceCurrentlyImposesNothingAndCountsNothing()
    {
        // Storm Giant's Thunderbolt, verbatim. ConditionPattern only recognises the
        // singular "the X condition" shape, so "the target has the Blinded and Deafened
        // conditions" — plural, two names — matches nothing at all: no rider is imposed
        // for either condition, and because the sentence still contains "Hit:" the whole
        // entry is credited as fully modelled anyway. Fix: at minimum this must not read
        // as zero-residue; ideally both riders should be imposed.
        var entry = EntryMechanicsParser.Classify(
            "Thunderbolt",
            MonsterEntrySection.Action,
            "Ranged Attack Roll: +14, range 500 ft. Hit: 22 (2d12 + 9) Lightning damage, and the " +
            "target has the Blinded and Deafened conditions until the start of the giant's next " +
            "turn.");

        Assert.Empty(entry.AppliedConditions);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void Issue373_AnUnexecutedDeathRiderBehindAFailureLabelCurrentlyCountsAsNothing()
    {
        // Will-o'-Wisp's Consume Life, verbatim. "The target dies, and the wisp regains
        // 10 (3d6) Hit Points" is real, unexecuted mechanics — a kill-and-heal rider —
        // but the sentence starts with "Failure" so MatchesStructuredForm credits it
        // whole, and it vanishes from UnmodelledClauses exactly like the goblin's
        // conditional damage.
        var entry = EntryMechanicsParser.Classify(
            "Consume Life",
            MonsterEntrySection.BonusAction,
            "Constitution Saving Throw: DC 10, one living creature the wisp can see within 5 " +
            "feet that has 0 Hit Points. Failure: The target dies, and the wisp regains 10 (3d6) " +
            "Hit Points.");

        Assert.Empty(entry.UnmodelledClauses);
    }
}
