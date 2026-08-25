using SRDCombat.Core.Definitions;
using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// Pins of known-buggy behavior in <see cref="EntryMechanicsParser"/>, one issue per
/// fact. The span-coverage refactor (#382, docs/2026-08-24-span-accounting-design.md
/// §9.2) flipped the accounting halves of #371, #372 and #373: what used to be
/// silently credited to a label now lands in residue, computed by subtraction rather
/// than by a sentence-level credit test. #370 was different in kind — a
/// misattribution, not an omission, so coverage alone did not and could not fix it
/// (design §12.1) — and is now fixed on its own terms: <c>ParseSave</c> no longer
/// reads "Failure or Success:" anywhere in the text as governing the whole entry's
/// outcome.
/// </summary>
public sealed class KnownGapPinsTests
{
    [Fact]
    public void Issue371_AConditionalDamageAlternativeNowLandsInResidue()
    {
        // Swarm of Rats' Bites, verbatim. "or 2 (1d4) Piercing damage if the swarm is
        // Bloodied" is a real, printed conditional alternative that the model has no
        // vocabulary for. Fixed accounting half of #382: DamagePattern's loop breaks on
        // the "or"-alternative rather than claiming it, so the uncovered span now falls
        // out as residue by subtraction. #371's execution half — structuring the
        // Bloodied-conditional tier itself — stays open.
        var entry = EntryMechanicsParser.Classify(
            "Bites",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +2, reach 5 ft. Hit: 5 (2d4) Piercing damage, or 2 (1d4) Piercing " +
            "damage if the swarm is Bloodied.");

        var damage = Assert.Single(entry.Attack!.Damage);
        Assert.Equal(5, damage.PrintedAverage);
        Assert.Equal(["or 2 (1d4) Piercing damage if the swarm is Bloodied"], entry.UnmodelledClauses);
    }

    [Fact]
    public void Issue372_APluralConditionsSentenceNowCountsAsResidueWhileStillImposingNothing()
    {
        // Storm Giant's Thunderbolt, verbatim. ConditionPattern still only recognises
        // the singular "the X condition" shape, so "the target has the Blinded and
        // Deafened conditions" — plural, two names — still matches nothing and no rider
        // is imposed for either condition (#372's execution half, still open). What
        // flips is the accounting half only (design §9.2): nothing claims this
        // sentence any more, so it is no longer swallowed by the attack's "Hit:"
        // credit and shows up as residue instead of vanishing.
        var entry = EntryMechanicsParser.Classify(
            "Thunderbolt",
            MonsterEntrySection.Action,
            "Ranged Attack Roll: +14, range 500 ft. Hit: 22 (2d12 + 9) Lightning damage, and the " +
            "target has the Blinded and Deafened conditions until the start of the giant's next " +
            "turn.");

        Assert.Empty(entry.AppliedConditions);
        Assert.Equal(
            ["and the target has the Blinded and Deafened conditions until the start of the giant's next turn"],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void Issue373_AnUnexecutedDeathRiderBehindAFailureLabelNowLandsInResidue()
    {
        // Will-o'-Wisp's Consume Life, verbatim. "The target dies, and the wisp regains
        // 10 (3d6) Hit Points" is real, unexecuted mechanics — a kill-and-heal rider —
        // that used to vanish because the sentence starts with "Failure" and the old
        // accounting credited the whole sentence to that label. Fixed accounting half
        // of #382: nothing claims the death-and-heal clause, so it is residue.
        //
        // A second clause is new here too, and it is a different gap: this is a
        // single-target save entry, so under design §7.6 only the head noun "one
        // creature" is claimed — the distance ("within 5 feet") and the sight
        // qualifier ("the wisp can see") are printed rules `UseSaveEntry` does not
        // enforce (#386), so the whole qualifier clause is honest residue rather than
        // a false claim of range or sight the engine does not check.
        var entry = EntryMechanicsParser.Classify(
            "Consume Life",
            MonsterEntrySection.BonusAction,
            "Constitution Saving Throw: DC 10, one living creature the wisp can see within 5 " +
            "feet that has 0 Hit Points. Failure: The target dies, and the wisp regains 10 (3d6) " +
            "Hit Points.");

        Assert.Equal(
            [
                "one living creature the wisp can see within 5 feet that has 0 Hit Points",
                "The target dies, and the wisp regains 10 (3d6) Hit Points",
            ],
            entry.UnmodelledClauses);
    }
}
