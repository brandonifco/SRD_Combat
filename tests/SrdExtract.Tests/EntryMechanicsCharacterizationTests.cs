using SRDCombat.Core.Definitions;
using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// Characterization fixtures for <see cref="EntryMechanicsParser"/>, pinning its
/// current, doc-commented behavior before the span-coverage refactor (#382) touches
/// anything. Entry text is taken verbatim from <c>data/srd/monsters.json</c> wherever
/// a named example exists there — see each fixture's comment for its source.
/// </summary>
/// <remarks>
/// These are pins, not specifications: every assertion was checked against the
/// parser's actual current output before being written down. If #382 changes a
/// documented reading on purpose, the corresponding test here is expected to need
/// updating alongside it — that is what "safety net" means, not "frozen forever".
/// </remarks>
public sealed class EntryMechanicsCharacterizationTests
{
    #region Usage limits

    [Fact]
    public void ARechargeSixSuffixParsesToItsMinimum()
    {
        // Ankheg's Acid Spray, printed "Acid Spray (Recharge 6)".
        var entry = EntryMechanicsParser.Classify(
            "Acid Spray (Recharge 6)",
            MonsterEntrySection.Action,
            "Dexterity Saving Throw: DC 12, each creature in a 30-foot-long, 5-foot-wide Line. " +
            "Failure: 14 (4d6) Acid damage. Success: Half damage.");

        Assert.Equal(UsageLimitKind.Recharge, entry.Usage?.Kind);
        Assert.Equal(6, entry.Usage?.RechargeMinimum);
        Assert.Equal("Acid Spray", entry.Name);
    }

    [Fact]
    public void ARechargeRangeSuffixParsesToItsLowerBound()
    {
        // Behir's Lightning Breath, printed "Lightning Breath (Recharge 5-6)". The
        // stored text carries the source's own missing space ("5-footwide"), kept
        // verbatim rather than corrected.
        var entry = EntryMechanicsParser.Classify(
            "Lightning Breath (Recharge 5-6)",
            MonsterEntrySection.Action,
            "Dexterity Saving Throw: DC 16, each creature in a 90-foot-long, 5-footwide Line. " +
            "Failure: 66 (12d10) Lightning damage. Success: Half damage.");

        Assert.Equal(UsageLimitKind.Recharge, entry.Usage?.Kind);
        Assert.Equal(5, entry.Usage?.RechargeMinimum);
        Assert.Equal("Lightning Breath", entry.Name);
    }

    [Fact]
    public void APerDaySuffixParsesToItsCount()
    {
        // Mage's Misty Step, printed "Misty Step (3/Day)".
        var entry = EntryMechanicsParser.Classify(
            "Misty Step (3/Day)",
            MonsterEntrySection.Action,
            "The mage casts Misty Step, using the same spellcasting ability as Spellcasting.");

        Assert.Equal(UsageLimitKind.PerDay, entry.Usage?.Kind);
        Assert.Equal(3, entry.Usage?.UsesPerDay);
        Assert.Equal("Misty Step", entry.Name);
    }

    [Fact]
    public void ARechargeAfterRestSuffixParsesToItsOwnKind()
    {
        // Cloaker's Phantasms, printed "Phantasms (Recharge after a Short or Long Rest)".
        var entry = EntryMechanicsParser.Classify(
            "Phantasms (Recharge after a Short or Long Rest)",
            MonsterEntrySection.Action,
            "The cloaker casts the Mirror Image spell, requiring no spell components and using " +
            "Wisdom as the spellcasting ability. The spell ends early if the cloaker starts or " +
            "ends its turn in Bright Light.");

        Assert.Equal(UsageLimitKind.RechargeAfterRest, entry.Usage?.Kind);
        Assert.Null(entry.Usage?.RechargeMinimum);
        Assert.Null(entry.Usage?.UsesPerDay);
        Assert.Equal("Phantasms", entry.Name);
    }

    [Fact]
    public void ABareNameWithNoUsageSuffixCarriesNoUsageLimit()
    {
        var entry = EntryMechanicsParser.Classify(
            "Fist",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5, reach 5 ft. Hit: 5 (1d4 + 3) Bludgeoning damage.");

        Assert.Null(entry.Usage);
        Assert.Equal("Fist", entry.Name);
    }

    #endregion

    #region Attack + rider

    [Fact]
    public void AGrappledRiderWithAnEscapeDcAndSizeGateRidesTheAttack()
    {
        // Giant Frog's Bite.
        var entry = EntryMechanicsParser.Classify(
            "Bite",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +3, reach 5 ft. Hit: 5 (1d6 + 2) Piercing damage. If the target is " +
            "a Medium or smaller creature, it has the Grappled condition (escape DC 11).");

        Assert.Equal(EntryMechanics.Attack, entry.Mechanics);
        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Grappled, rider.Condition);
        Assert.Equal(11, rider.EscapeDifficultyClass);
        Assert.Equal(CreatureSize.Medium, rider.MaximumTargetSize);
        Assert.Null(rider.Duration);
        Assert.True(rider.IsFullyModelled);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void ASizeGatedProneRiderWithNoDurationIsFullyModelled()
    {
        // Gladiator's Shield Bash — a SavingThrow entry whose Failure clause carries the
        // same size-gated Prone shape an attack's Hit clause can.
        var entry = EntryMechanicsParser.Classify(
            "Shield Bash",
            MonsterEntrySection.Action,
            "Strength Saving Throw: DC 15, one creature within 5 feet that the gladiator can see. " +
            "Failure: 9 (2d4 + 4) Bludgeoning damage. If the target is a Medium or smaller " +
            "creature, it has the Prone condition.");

        Assert.Equal(EntryMechanics.SavingThrow, entry.Mechanics);
        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Prone, rider.Condition);
        Assert.Equal(CreatureSize.Medium, rider.MaximumTargetSize);
        Assert.Null(rider.Duration);
        Assert.True(rider.IsFullyModelled);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void ATwoConditionSentenceSplitsIntoGrappledPlusRestrainedUntilTheGrappleEnds()
    {
        // Purple Worm's Bite.
        var entry = EntryMechanicsParser.Classify(
            "Bite",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +14, reach 10 ft. Hit: 22 (3d8 + 9) Piercing damage. If the target " +
            "is a Large or smaller creature, it has the Grappled condition (escape DC 19), and it " +
            "has the Restrained condition until the grapple ends.");

        Assert.Equal(2, entry.AppliedConditions.Count);

        var grappled = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Grappled);
        Assert.Equal(19, grappled.EscapeDifficultyClass);
        Assert.Equal(CreatureSize.Large, grappled.MaximumTargetSize);
        Assert.True(grappled.IsFullyModelled);

        var restrained = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Restrained);
        Assert.NotNull(restrained.Duration);
        Assert.True(restrained.Duration!.WhileGrappleHolds);
        Assert.True(restrained.IsFullyModelled);

        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheWaterElementalWhelmsGrappledRidesWhileItsRestrainedIsRefused()
    {
        // Water Elemental's Whelm: the Grappled rider is bare (no trailing text) and
        // rides fully modelled. The Restrained sentence chains suffocation and recurring
        // damage the model cannot express, so ReadRider refuses it — the rider still
        // appears on AppliedConditions (its condition and clause are recognised), but
        // with UnmodelledRequirement set to the whole sentence and no Duration, so
        // ConditionRules.CanBeImposed is false for it and it is never actually imposed
        // at runtime.
        var entry = EntryMechanicsParser.Classify(
            "Whelm",
            MonsterEntrySection.Action,
            "Strength Saving Throw: DC 15, each creature in the elemental's space. Failure: 22 " +
            "(4d8 + 4) Bludgeoning damage. If the target is a Large or smaller creature, it has " +
            "the Grappled condition (escape DC 14). Until the grapple ends, the target has the " +
            "Restrained condition, is suffocating unless it can breathe water, and takes 9 (2d8) " +
            "Bludgeoning damage at the start of each of the elemental's turns. The elemental can " +
            "grapple one Large creature or up to two Medium or smaller creatures at a time with " +
            "Whelm. As an action, a creature within 5 feet of the elemental can pull a creature " +
            "out of it by succeeding on a DC 14 Strength (Athletics) check. Success: Half damage " +
            "only.");

        Assert.Equal(2, entry.AppliedConditions.Count);

        var grappled = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Grappled);
        Assert.Equal(14, grappled.EscapeDifficultyClass);
        Assert.Equal(CreatureSize.Large, grappled.MaximumTargetSize);
        Assert.True(grappled.IsFullyModelled);

        var restrained = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Restrained);
        Assert.False(restrained.IsFullyModelled);
        Assert.Null(restrained.Duration);
        Assert.NotNull(restrained.UnmodelledRequirement);
        Assert.Contains("suffocating", restrained.UnmodelledRequirement, StringComparison.Ordinal);

        // The unexpressed sentence remains counted in the entry's residue too.
        Assert.Contains(
            entry.UnmodelledClauses,
            clause => clause.Contains("suffocating", StringComparison.Ordinal));
    }

    [Fact]
    public void ARiderWithTrailingTextThatIsNotADurationIsRefusedWholeSentence()
    {
        // Roper's Tentacle: "from one of six tentacles" is limb bookkeeping, not a
        // recognised duration, so the Grappled rider is refused — and because it is
        // refused, the sibling Restrained rider tied to it ("until the grapple ends")
        // is refused too, per the sibling-grapple rule.
        var entry = EntryMechanicsParser.Classify(
            "Tentacle",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +7, reach 60 ft. Hit: The target has the Grappled condition " +
            "(escape DC 14) from one of six tentacles, and the target has the Poisoned condition " +
            "until the grapple ends. The tentacle can be damaged, freeing a creature it has " +
            "Grappled when destroyed (AC 20, HP 10, Immunity to Poison and Psychic damage). " +
            "Damaging the tentacle deals no damage to the roper, and a destroyed tentacle " +
            "regrows at the start of the roper's next turn.");

        Assert.Equal(2, entry.AppliedConditions.Count);

        var grappled = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Grappled);
        Assert.False(grappled.IsFullyModelled);
        Assert.NotNull(grappled.UnmodelledRequirement);

        var poisoned = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Poisoned);
        Assert.False(poisoned.IsFullyModelled);
        Assert.Null(poisoned.Duration);
        Assert.NotNull(poisoned.UnmodelledRequirement);
    }

    #endregion

    #region Durations

    [Fact]
    public void UntilTheStartOfItsNextTurnIsStartOfTurnOwnedByTheBearer()
    {
        var entry = EntryMechanicsParser.Classify(
            "Test Attack",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5, reach 5 ft. Hit: 5 (1d6 + 2) Piercing damage, and the target " +
            "has the Poisoned condition until the start of its next turn.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.NotNull(rider.Duration);
        Assert.Equal(ConditionClock.StartOfTurn, rider.Duration!.Clock);
        Assert.Equal(ConditionDurationOwner.Bearer, rider.Duration.Owner);
        Assert.True(rider.IsFullyModelled);
    }

    [Fact]
    public void UntilTheEndOfItsNextTurnIsEndOfTurnOwnedByTheBearer()
    {
        var entry = EntryMechanicsParser.Classify(
            "Test Attack",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5, reach 5 ft. Hit: 5 (1d6 + 2) Piercing damage, and the target " +
            "has the Frightened condition until the end of its next turn.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.NotNull(rider.Duration);
        Assert.Equal(ConditionClock.EndOfTurn, rider.Duration!.Clock);
        Assert.Equal(ConditionDurationOwner.Bearer, rider.Duration.Owner);
    }

    [Fact]
    public void ANamedSourcePossessiveDurationIsOwnedBySource()
    {
        // Bearded Devil's Beard.
        var entry = EntryMechanicsParser.Classify(
            "Beard",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5, reach 5 ft. Hit: 7 (1d8 + 3) Piercing damage, and the target " +
            "has the Poisoned condition until the start of the devil's next turn. Until this " +
            "poison ends, the target can't regain Hit Points.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Poisoned, rider.Condition);
        Assert.NotNull(rider.Duration);
        Assert.Equal(ConditionClock.StartOfTurn, rider.Duration!.Clock);
        Assert.Equal(ConditionDurationOwner.Source, rider.Duration.Owner);
        Assert.True(rider.IsFullyModelled);

        // "Until this poison ends, the target can't regain Hit Points." is its own
        // sentence and is a real, unexpressed rule.
        Assert.Contains(
            entry.UnmodelledClauses,
            clause => clause.Contains("can't regain Hit Points", StringComparison.Ordinal));
    }

    [Fact]
    public void ForOneMinuteIsTenOfTheBearersTurns()
    {
        var entry = EntryMechanicsParser.ClassifyTrait(
            "Test Trait",
            "Wisdom Saving Throw: DC 10, one creature. Failure: The target has the Charmed " +
            "condition for 1 minute.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.NotNull(rider.Duration);
        Assert.Equal(10, rider.Duration!.TurnsAhead);
        Assert.False(rider.Duration.OutlastsFight);
    }

    [Fact]
    public void ForOneHourOutlastsTheFight()
    {
        var entry = EntryMechanicsParser.ClassifyTrait(
            "Test Trait",
            "Wisdom Saving Throw: DC 10, one creature. Failure: The target has the Charmed " +
            "condition for 1 hour.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.NotNull(rider.Duration);
        Assert.True(rider.Duration!.OutlastsFight);
    }

    [Fact]
    public void ADurationWithAnExtraEarlyOutIsRefusedRatherThanPartiallyMatched()
    {
        var entry = EntryMechanicsParser.ClassifyTrait(
            "Test Trait",
            "Wisdom Saving Throw: DC 10, one creature. Failure: The target has the Charmed " +
            "condition for 1 minute, until it takes damage, or until the charmer dies.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Null(rider.Duration);
        Assert.False(rider.IsFullyModelled);
        Assert.NotNull(rider.UnmodelledRequirement);
    }

    #endregion

    #region Repeat saves

    [Fact]
    public void TheQuasitScareTwoSentenceFormRepeatSavesUpToOneMinute()
    {
        var entry = EntryMechanicsParser.Classify(
            "Scare",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 10, one creature within 20 feet. Failure: The target has the " +
            "Frightened condition. At the end of each of its turns, the target repeats the save, " +
            "ending the effect on itself on a success. After 1 minute, it succeeds automatically.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Frightened, rider.Condition);
        Assert.NotNull(rider.Duration);
        Assert.True(rider.Duration!.RepeatSaveAtTurnEnd);
        Assert.Equal(10, rider.Duration.TurnsAhead);
        Assert.True(rider.IsFullyModelled);
    }

    [Fact]
    public void TheSameRiderWithoutTheAutomaticSuccessCapIsRefused()
    {
        // The Quasit's Scare text, with its printed "After 1 minute, it succeeds
        // automatically." cap removed — ReadRider requires that exact sentence
        // somewhere in the entry before it will read the repeat-save trailing clause
        // as a duration at all.
        var entry = EntryMechanicsParser.Classify(
            "Scare",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 10, one creature within 20 feet. Failure: The target has the " +
            "Frightened condition. At the end of each of its turns, the target repeats the save, " +
            "ending the effect on itself on a success.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Null(rider.Duration);
        Assert.False(rider.IsFullyModelled);
        Assert.NotNull(rider.UnmodelledRequirement);
    }

    [Fact]
    public void TheDoppelgangerInSentenceFormAlsoRepeatSavesUpToOneMinute()
    {
        var entry = EntryMechanicsParser.Classify(
            "Unsettling Visage",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 12, each creature in a 15-foot Emanation originating from the " +
            "doppelganger that can see the doppelganger. Failure: The target has the Frightened " +
            "condition and repeats the save at the end of each of its turns, ending the effect on " +
            "itself on a success. After 1 minute, it succeeds automatically.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Frightened, rider.Condition);
        Assert.NotNull(rider.Duration);
        Assert.True(rider.Duration!.RepeatSaveAtTurnEnd);
        Assert.True(rider.IsFullyModelled);
    }

    [Fact]
    public void ARiderWithTrailingTextBeforeTheStandaloneRepeatSentenceDoesNotAnnex()
    {
        // Not a real corpus entry — the annex rule (design §5.2) requires the rider's
        // own trailing text to be empty, not merely "carries no duration this engine
        // recognises". Here the rider trails off with "until it takes damage" before
        // the standalone repeat-save sentence that would otherwise annex cleanly, and
        // that early out is a rule of its own the model cannot express — so the rider
        // must stay refused rather than reading the adjacent sentence as its clock.
        // This is the loose-but-not-strict shape the corpus itself never prints (on
        // the closed corpus the annex window contains the Quasit alone), pinning the
        // precondition against a future loosening nothing in data/srd would catch.
        var entry = EntryMechanicsParser.Classify(
            "Scare",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 10, one creature within 20 feet. Failure: The target has the " +
            "Frightened condition until it takes damage. At the end of each of its turns, the " +
            "target repeats the save, ending the effect on itself on a success. After 1 minute, " +
            "it succeeds automatically.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Null(rider.Duration);
        Assert.False(rider.IsFullyModelled);
        Assert.NotNull(rider.UnmodelledRequirement);
    }

    [Fact]
    public void ASentenceWithASiblingClauseDoesNotAnnexEvenWithEmptyTrailingText()
    {
        // Not a real corpus entry — a second axis the annex rule (design §5.2) must
        // stay tight on, distinct from the previous fixture's non-empty trailing text.
        // Here the FIRST rider's own clause genuinely has empty trailing text (nothing
        // follows "Frightened condition" before RiderClausePattern's split), but it is
        // not the whole sentence — a second clause, "and it has the Charmed
        // condition", shares it. The deleted RepeatSaveJoinPattern's lookbehind
        // required its match to fall immediately after "Failure: The target has the
        // <Condition> condition" with nothing else in the sentence before the period,
        // so a sentence naming two conditions would never have matched it — only the
        // clause boundary made this rider's own trailing look empty, not the sentence
        // ending there. Both riders must stay refused.
        var entry = EntryMechanicsParser.Classify(
            "Test Gaze",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 10, one creature within 20 feet. Failure: The target has the " +
            "Frightened condition, and it has the Charmed condition. At the end of each of its " +
            "turns, the target repeats the save, ending the effect on itself on a success. After " +
            "1 minute, it succeeds automatically.");

        Assert.Equal(2, entry.AppliedConditions.Count);

        var frightened = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Frightened);
        Assert.Null(frightened.Duration);
        Assert.False(frightened.IsFullyModelled);

        var charmed = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Charmed);
        Assert.Null(charmed.Duration);
        Assert.False(charmed.IsFullyModelled);
    }

    #endregion

    #region Tiers

    [Fact]
    public void ASecondFailureRiderIsRefused()
    {
        // Brass Dragon Wyrmling's Sleep Breath — the exact shape CLAUDE.md names: a
        // rider behind a deeper failure tier must not land on the plain first failure.
        // Both conditions are recognised (they appear on AppliedConditions), but neither
        // is fully modelled: the Incapacitated clause carries an extra trailing clause
        // ("at which point it repeats the save") that ParseDuration does not recognise,
        // and the Unconscious clause is refused outright by the TieredFailurePattern
        // check ("Second Failure:"). Neither is ever imposable
        // (ConditionRules.CanBeImposed requires IsFullyModelled), which is the effective
        // "refused" the SRD reading demands.
        var entry = EntryMechanicsParser.Classify(
            "Sleep Breath",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 11, each creature in a 15-foot Cone. Failure: The " +
            "target has the Incapacitated condition until the end of its next turn, at which " +
            "point it repeats the save. Second Failure: The target has the Unconscious condition " +
            "for 1 minute. This effect ends for the target if it takes damage or a creature " +
            "within 5 feet of it takes an action to wake it.");

        var incapacitated = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Incapacitated);
        Assert.False(incapacitated.IsFullyModelled);

        var unconscious = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Unconscious);
        Assert.False(unconscious.IsFullyModelled);
        Assert.Contains("Second Failure:", unconscious.UnmodelledRequirement, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePetrifyingGazePairStructuresAsOneEscalatingRestrainedRider()
    {
        // Basilisk's Petrifying Gaze, verbatim.
        var entry = EntryMechanicsParser.Classify(
            "Petrifying Gaze",
            MonsterEntrySection.BonusAction,
            "Constitution Saving Throw: DC 12, each creature in a 30-foot Cone. If the basilisk " +
            "sees its reflection in the Cone, the basilisk must make this save. First Failure: " +
            "The target has the Restrained condition and repeats the save at the end of its next " +
            "turn if it is still Restrained, ending the effect on itself on a success. Second " +
            "Failure: The target has the Petrified condition instead of the Restrained condition.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Restrained, rider.Condition);
        Assert.Equal(ConditionType.Petrified, rider.EscalatesTo);
        Assert.NotNull(rider.Duration);
        Assert.True(rider.Duration!.RepeatSaveAtTurnEnd);
        Assert.True(rider.Duration.OutlastsFight);
        Assert.True(rider.IsFullyModelled);
    }

    #endregion

    #region Embedded save

    [Fact]
    public void TheGhastClawStructuresAWholeEmbeddedSave()
    {
        var entry = EntryMechanicsParser.Classify(
            "Claw",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5, reach 5 ft. Hit: 10 (2d6 + 3) Slashing damage. If the target " +
            "is a non-Undead creature, it is subjected to the following effect. Constitution " +
            "Saving Throw: DC 10. Failure: The target has the Paralyzed condition until the end " +
            "of its next turn.");

        Assert.Equal(EntryMechanics.Attack, entry.Mechanics);
        Assert.NotNull(entry.Attack);
        Assert.NotNull(entry.Attack!.EmbeddedSave);
        Assert.Equal(CreatureType.Undead, entry.Attack.EmbeddedSave!.ExcludedTargetType);
        Assert.Equal(10, entry.Attack.EmbeddedSave.Save.DifficultyClass);

        var embeddedRider = Assert.Single(entry.Attack.EmbeddedSave.Save.AppliedConditions);
        Assert.Equal(ConditionType.Paralyzed, embeddedRider.Condition);

        // The rider is the embedded save's, not the attack's own — it must not also
        // appear on the entry's top-level AppliedConditions.
        Assert.Empty(entry.AppliedConditions);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheGhoulClawIsOneWordBeyondTheEmbeddedSaveTemplateAndStaysRefused()
    {
        var entry = EntryMechanicsParser.Classify(
            "Claw",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +4, reach 5 ft. Hit: 4 (1d4 + 2) Slashing damage. If the target " +
            "is a creature that isn't an Undead or elf, it is subjected to the following effect. " +
            "Constitution Saving Throw: DC 10. Failure: The target has the Paralyzed condition " +
            "until the end of its next turn.");

        Assert.Null(entry.Attack!.EmbeddedSave);
        Assert.NotEmpty(entry.UnmodelledClauses);

        // The attack-entry "Failure:" rule (ReadRider) refuses this Paralyzed rider
        // outright because it sits inside an Attack entry and does not belong to a
        // structured EmbeddedAttackSave — it still appears on AppliedConditions, but
        // not fully modelled, so it is never imposed at runtime.
        var paralyzed = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Paralyzed);
        Assert.False(paralyzed.IsFullyModelled);
    }

    #endregion

    #region Head clauses

    [Fact]
    public void ABalorStylePullAndProneSentenceRefusesTheRiderWithTheUnmodelledCompanion()
    {
        // Balor's Flame Whip: the head clause "the balor pulls the target up to 25 feet
        // straight toward itself" is not accounted for by anything else in the entry, so
        // the whole sentence is recorded as the Prone rider's UnmodelledRequirement
        // rather than imposing only the condition and silently dropping the pull — the
        // rider is present on AppliedConditions but not fully modelled, so it is never
        // actually imposed at runtime.
        var entry = EntryMechanicsParser.Classify(
            "Flame Whip",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +14, reach 30 ft. Hit: 18 (3d6 + 8) Force damage plus 17 (5d6) " +
            "Fire damage. If the target is a Huge or smaller creature, the balor pulls the target " +
            "up to 25 feet straight toward itself, and the target has the Prone condition.");

        var prone = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Prone, prone.Condition);
        Assert.False(prone.IsFullyModelled);
        Assert.Contains("pulls the target", prone.UnmodelledRequirement, StringComparison.Ordinal);
    }

    #endregion

    #region Multiattack

    [Fact]
    public void ASimpleMultiattackRecordsCountAndSingleAttackName()
    {
        // Ape's Multiattack.
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The ape makes two Fist attacks.");

        Assert.Equal(EntryMechanics.Multiattack, entry.Mechanics);
        Assert.NotNull(entry.Multiattack);
        Assert.Equal(2, entry.Multiattack!.AttackCount);
        Assert.Equal(["Fist"], entry.Multiattack.AttackNames);
        Assert.False(entry.Multiattack.AnyCombination);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void ACombinationFormMultiattackRecordsAnyCombinationTrue()
    {
        // Bandit Captain's Multiattack.
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The bandit makes two attacks, using Scimitar and Pistol in any combination.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(2, entry.Multiattack!.AttackCount);
        Assert.Equal(["Scimitar", "Pistol"], entry.Multiattack.AttackNames);
        Assert.True(entry.Multiattack.AnyCombination);
    }

    [Fact]
    public void TheBeardedDevilSumsTwoNamedSingleAttackClauses()
    {
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The devil makes one Beard attack and one Infernal Glaive attack.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(2, entry.Multiattack!.AttackCount);
        Assert.Equal(["Beard", "Infernal Glaive"], entry.Multiattack.AttackNames);
        // Two named attacks means the creature picks between them, even though the
        // printed text is "one of each" rather than "in any combination".
        Assert.True(entry.Multiattack.AnyCombination);
    }

    [Fact]
    public void TheClayGolemsAlternativeCompositionKeepsTheFirstBranchAndCountsTheSecond()
    {
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The golem makes two Slam attacks, or it makes three Slam attacks if it used Hasten " +
            "this turn.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(2, entry.Multiattack!.AttackCount);
        Assert.Equal(["Slam"], entry.Multiattack.AttackNames);
        Assert.False(entry.Multiattack.AnyCombination);

        Assert.Contains(
            entry.UnmodelledClauses,
            clause => clause.Contains("three Slam attacks", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMummysBundledUseIsCountedAlongsideAFullyMatchedComposition()
    {
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The mummy makes two Rotting Fist attacks and uses Dreadful Glare.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(2, entry.Multiattack!.AttackCount);
        Assert.Equal(["Rotting Fist"], entry.Multiattack.AttackNames);

        Assert.Contains(
            entry.UnmodelledClauses,
            clause => clause.Contains("uses Dreadful Glare", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRopersMidSentenceBundledUseIsCountedToo()
    {
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The roper makes two Tentacle attacks, uses Reel, and makes two Bite attacks.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(4, entry.Multiattack!.AttackCount);
        Assert.Equal(["Tentacle", "Bite"], entry.Multiattack.AttackNames);

        // BundledMultiattackUseClauses capitalizes the connector-less bare-comma form.
        Assert.Contains("Uses Reel.", entry.UnmodelledClauses);
    }

    [Fact]
    public void AMultiattackWhoseSecondSentenceDoesNotMatchTheCompositionIsCounted()
    {
        // Gladiator's Multiattack: the composition sentence is fully expressed, but the
        // replacement option in the second sentence is not, and DescribesTheComposition
        // never sees it because ParseMultiattack only reads the first sentence.
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The gladiator makes three Spear attacks. It can replace one attack with a use of " +
            "Shield Bash.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(3, entry.Multiattack!.AttackCount);
        Assert.Equal(["Spear"], entry.Multiattack.AttackNames);

        Assert.Contains(
            entry.UnmodelledClauses,
            clause => clause.Contains("replace one attack", StringComparison.Ordinal));
    }

    #endregion

    #region Saves

    [Fact]
    public void AFullSaveHeaderParsesAbilityDcAreaAndDamage()
    {
        // Ankheg's Acid Spray.
        var entry = EntryMechanicsParser.Classify(
            "Acid Spray",
            MonsterEntrySection.Action,
            "Dexterity Saving Throw: DC 12, each creature in a 30-foot-long, 5-foot-wide Line. " +
            "Failure: 14 (4d6) Acid damage. Success: Half damage.");

        Assert.Equal(EntryMechanics.SavingThrow, entry.Mechanics);
        Assert.NotNull(entry.Save);
        Assert.Equal(Ability.Dexterity, entry.Save!.Ability);
        Assert.Equal(12, entry.Save.DifficultyClass);
        Assert.NotNull(entry.Save.Area);
        Assert.Equal(AreaShape.Line, entry.Save.Area!.Shape);
        Assert.Equal(30, entry.Save.Area.SizeFeet);
        Assert.Equal(5, entry.Save.Area.WidthFeet);

        var damage = Assert.Single(entry.Save.FailureDamage);
        Assert.Equal(DamageType.Acid, damage.Type);
        Assert.Equal(14, damage.PrintedAverage);

        Assert.Equal(SaveSuccessOutcome.HalfDamage, entry.Save.SuccessOutcome);
    }

    [Fact]
    public void AConeAreaParses()
    {
        // Basilisk's Petrifying Gaze header.
        var entry = EntryMechanicsParser.Classify(
            "Test Save",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 12, each creature in a 30-foot Cone. Failure: 5 (2d4) " +
            "Fire damage. Success: Half damage.");

        Assert.NotNull(entry.Save?.Area);
        Assert.Equal(AreaShape.Cone, entry.Save!.Area!.Shape);
        Assert.Equal(30, entry.Save.Area.SizeFeet);
        Assert.Null(entry.Save.Area.WidthFeet);
    }

    [Fact]
    public void ALongWideLineAreaParsesBothDimensions()
    {
        var entry = EntryMechanicsParser.Classify(
            "Test Save",
            MonsterEntrySection.Action,
            "Dexterity Saving Throw: DC 16, each creature in a 60-foot-long, 5-foot-wide Line. " +
            "Failure: 20 (4d8) Cold damage. Success: Half damage.");

        Assert.NotNull(entry.Save?.Area);
        Assert.Equal(AreaShape.Line, entry.Save!.Area!.Shape);
        Assert.Equal(60, entry.Save.Area.SizeFeet);
        Assert.Equal(5, entry.Save.Area.WidthFeet);
    }

    [Fact]
    public void FlatDamageWithNoDiceParsesAsAFlatExpression()
    {
        var entry = EntryMechanicsParser.Classify(
            "Test Save",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 10, one creature. Failure: 1 Piercing damage.");

        var damage = Assert.Single(entry.Save!.FailureDamage);
        Assert.Equal(1, damage.PrintedAverage);
        Assert.Equal(DamageType.Piercing, damage.Type);
        Assert.Equal(1, damage.Amount.Average);
    }

    [Fact]
    public void SuccessHalfDamageParsesToHalfDamageOutcome()
    {
        var entry = EntryMechanicsParser.Classify(
            "Test Save",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 10, one creature. Failure: 5 (2d4) Fire damage. " +
            "Success: Half damage.");

        Assert.Equal(SaveSuccessOutcome.HalfDamage, entry.Save!.SuccessOutcome);
    }

    [Fact]
    public void NoSuccessClauseParsesToNoEffect()
    {
        // Mummy's Dreadful Glare — the Success clause says something other than "Half
        // damage", so it does not match the HalfDamage check and falls to NoEffect.
        var entry = EntryMechanicsParser.Classify(
            "Dreadful Glare",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 11, one creature the mummy can see within 60 feet. Failure: " +
            "The target has the Frightened condition until the end of the mummy's next turn. " +
            "Success: The target is immune to this mummy's Dreadful Glare for 24 hours.");

        Assert.Equal(SaveSuccessOutcome.NoEffect, entry.Save!.SuccessOutcome);
    }

    #endregion

    #region Accounting

    [Fact]
    public void AnUnmodelledEntryCountsEverySentenceUnfiltered()
    {
        var entry = EntryMechanicsParser.Classify(
            "Berserk",
            MonsterEntrySection.Trait,
            "Whenever the golem starts its turn Bloodied, roll 1d6. On a 6, the golem goes " +
            "berserk. On each of its turns while berserk, the golem attacks the nearest creature " +
            "it can see.");

        Assert.Equal(EntryMechanics.Unmodelled, entry.Mechanics);
        Assert.Equal(3, entry.UnmodelledClauses.Count);
    }

    [Fact]
    public void AmphibiousClassifiesAsNarrativeWithNoClauses()
    {
        var entry = EntryMechanicsParser.Classify(
            "Amphibious",
            MonsterEntrySection.Trait,
            "The aboleth can breathe air and water.");

        Assert.Equal(EntryMechanics.Narrative, entry.Mechanics);
        Assert.Empty(entry.UnmodelledClauses);
        Assert.Empty(entry.AppliedConditions);
    }

    [Fact]
    public void PackTacticsInTraitSectionClassifiesAsPassive()
    {
        // Hell Hound's Pack Tactics, verbatim.
        var entry = EntryMechanicsParser.Classify(
            "Pack Tactics",
            MonsterEntrySection.Trait,
            "The hound has Advantage on an attack roll against a creature if at least one of the " +
            "hound's allies is within 5 feet of the creature and the ally doesn't have the " +
            "Incapacitated condition.");

        Assert.Equal(EntryMechanics.Passive, entry.Mechanics);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void ARiderSentenceWhoseConditionIsImposableIsAccountedFor()
    {
        var entry = EntryMechanicsParser.Classify(
            "Test Attack",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5, reach 5 ft. Hit: 5 (1d6 + 2) Piercing damage, and the target " +
            "has the Poisoned condition until the start of its next turn.");

        // The whole entry is fully modelled — the rider sentence is not left over.
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void SentenceSplittingDoesNotBreakOnFtAbbreviation()
    {
        // "reach 5 ft. Hit:" must not be read as a sentence boundary.
        var entry = EntryMechanicsParser.Classify(
            "Test Attack",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5, reach 5 ft. Hit: 5 (1d6 + 2) Piercing damage.");

        Assert.NotNull(entry.Attack);
        Assert.Equal(5, entry.Attack!.ReachFeet);
        Assert.Empty(entry.UnmodelledClauses);
    }

    #endregion

    #region ClassifyTrait

    [Fact]
    public void ASaveShapedTraitClassifiesAsSavingThrow()
    {
        var trait = EntryMechanicsParser.ClassifyTrait(
            "Test Breath",
            "Dexterity Saving Throw: DC 12, each creature in a 15-foot Cone. Failure: 10 (3d6) " +
            "Fire damage. Success: Half damage.");

        Assert.Equal(EntryMechanics.SavingThrow, trait.Mechanics);
        Assert.NotNull(trait.Save);
    }

    [Fact]
    public void WaterBreathingWithConsultInertListTrueClassifiesAsNarrative()
    {
        // Giant Octopus's own entry, verbatim.
        var trait = EntryMechanicsParser.ClassifyTrait(
            "Water Breathing",
            "The octopus can breathe only underwater. It can hold its breath for 1 hour outside " +
            "water.",
            consultInertList: true);

        Assert.Equal(EntryMechanics.Narrative, trait.Mechanics);
    }

    [Fact]
    public void WaterBreathingWithConsultInertListFalseClassifiesAsUnmodelled()
    {
        // The #349 reading: SpellParser passes false because the inert list was never
        // curated about spell prose, and the name collision is not a reading of the
        // spell.
        var trait = EntryMechanicsParser.ClassifyTrait(
            "Water Breathing",
            "The octopus can breathe only underwater. It can hold its breath for 1 hour outside " +
            "water.",
            consultInertList: false);

        Assert.Equal(EntryMechanics.Unmodelled, trait.Mechanics);
        Assert.NotEmpty(trait.UnmodelledClauses);
    }

    #endregion
}
