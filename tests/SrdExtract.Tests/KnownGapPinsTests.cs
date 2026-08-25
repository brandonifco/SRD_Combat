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
/// outcome. #372 is fixed on its own terms too — <c>PluralConditionPattern</c>
/// recognises "the X and Y conditions" and each name flows through the existing
/// imposability gates independently — and its former pin now lives as a
/// characterization fixture in <c>EntryMechanicsCharacterizationTests</c>'s "Plural
/// conditions (#372)" region instead of here. #371 is fixed on its own terms too —
/// <c>AlternativeDamagePattern</c> structures the "or…if" tier for the three
/// conditions the engine can check at attack resolution, and its former pin now
/// lives as a characterization fixture in <c>EntryMechanicsCharacterizationTests</c>'s
/// "Alternative damage (#371)" region instead of here.
/// </summary>
public sealed class KnownGapPinsTests
{
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
