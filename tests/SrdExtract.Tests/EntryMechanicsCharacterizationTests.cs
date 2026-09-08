using SRDCombat.Content;
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
    // #600: loaded once for the corpus-driven agreement test below, same pattern as
    // CorpusRoundTripTests's own field — the committed corpus is the fixture set, no
    // PDF needed.
    private static readonly IReadOnlyList<MonsterDefinition> Monsters =
        ContentLoader.Load(RepositoryPaths.SrdContentDirectory).Monsters;

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

        // The rider itself is fully modelled (nothing changes there). This is a
        // single-target save entry, so the printed distance is now claimed —
        // ReadRange structures "within 5 feet" onto SaveEffect.RangeFeet (#386) — but
        // the sight qualifier stays permanently unenforced (the standing no-sight-
        // model reading), and "one creature" itself is gated by the "that…" clause
        // following it (design §7.6's own negative lookahead), so it is not claimed
        // by SaveTargetClausePattern either. Both survive as residue, split into two
        // chunks by the now-claimed "within 5 feet" that used to sit between them.
        Assert.Equal(5, entry.Save!.RangeFeet);
        Assert.Equal(
            ["one creature", "that the gladiator can see"],
            entry.UnmodelledClauses);
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

    #region Alternative damage (#371)

    [Fact]
    public void AnOrIfAlternativeReplacesTheBaseDamageWhenTheAttackRollHadAdvantage()
    {
        // Chimera's Bite, verbatim. "or 18 (4d6 + 4) Piercing damage if the chimera
        // had Advantage on the attack roll" is the same rule as the goblins' "plus…if
        // the attack roll had Advantage" rider, worded differently and printed as a
        // replacement rather than an addition — AttackRulesTests pins the runtime
        // distinction between the two shapes.
        var entry = EntryMechanicsParser.Classify(
            "Bite",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +7, reach 5 ft. Hit: 11 (2d6 + 4) Piercing damage, or 18 (4d6 + 4) " +
            "Piercing damage if the chimera had Advantage on the attack roll.");

        Assert.NotNull(entry.Attack!.Alternative);
        Assert.Equal(18, entry.Attack.Alternative!.PrintedAverage);
        Assert.Equal(DamageType.Piercing, entry.Attack.Alternative.Type);
        Assert.Equal(AttackDamageCondition.AttackRollHadAdvantage, entry.Attack.Alternative.Condition);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void AnOrIfAlternativeReplacesTheBaseDamageWhenTheAttackerIsBloodied()
    {
        // Swarm of Rats' Bites, verbatim. The condition reads the swarm's own Hit
        // Points — AttackDamageCondition.AttackerIsBloodied, not TargetIsBloodied.
        var entry = EntryMechanicsParser.Classify(
            "Bites",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +2, reach 5 ft. Hit: 5 (2d4) Piercing damage, or 2 (1d4) Piercing " +
            "damage if the swarm is Bloodied.");

        Assert.NotNull(entry.Attack!.Alternative);
        Assert.Equal(2, entry.Attack.Alternative!.PrintedAverage);
        Assert.Equal(AttackDamageCondition.AttackerIsBloodied, entry.Attack.Alternative.Condition);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void AnOrIfAlternativeReplacesTheBaseDamageWhenTheTargetIsBloodied()
    {
        // Blood Hawk's Beak, verbatim — the opposite reading from the swarms above.
        var entry = EntryMechanicsParser.Classify(
            "Beak",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +4, reach 5 ft. Hit: 4 (1d4 + 2) Piercing damage, or 6 (1d8 + 2) " +
            "Piercing damage if the target is Bloodied.");

        Assert.NotNull(entry.Attack!.Alternative);
        Assert.Equal(6, entry.Attack.Alternative!.PrintedAverage);
        Assert.Equal(AttackDamageCondition.TargetIsBloodied, entry.Attack.Alternative.Condition);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheSwarmOfVenomousSnakesEmDashOrPlusChainStructuresBothTiersAndTheUnconditionalPoison()
    {
        // Swarm of Venomous Snakes' Bites, verbatim as extracted (#409, SRD 5.2.1
        // p. 363). The printed em dashes survive extraction as ASCII hyphens
        // (damage-or, Bloodied-plus). The Piercing base alternates on the swarm's own
        // Bloodied state, and the Poison is added unconditionally regardless of tier —
        // AttackRulesTests pins the combined runtime roll. This is one of the corpus's
        // only two em-dash instances, and #371 left it as residue.
        var entry = EntryMechanicsParser.Classify(
            "Bites",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +6, reach 5 ft. Hit: 8 (1d8 + 4) Piercing damage-or 6 (1d4 + 4) " +
            "Piercing damage if the swarm is Bloodied-plus 10 (3d6) Poison damage.");

        // Two unconditional base components in printed order: the Piercing base and the
        // always-on Poison "plus". Neither carries a Condition — the condition lives on
        // the Alternative, which stands in for only the Piercing.
        Assert.Collection(
            entry.Attack!.Damage,
            piercing =>
            {
                Assert.Equal(DamageType.Piercing, piercing.Type);
                Assert.Equal(8, piercing.PrintedAverage);
                Assert.Null(piercing.Condition);
            },
            poison =>
            {
                Assert.Equal(DamageType.Poison, poison.Type);
                Assert.Equal(10, poison.PrintedAverage);
                Assert.Null(poison.Condition);
            });

        Assert.NotNull(entry.Attack.Alternative);
        Assert.Equal(6, entry.Attack.Alternative!.PrintedAverage);
        Assert.Equal(DamageType.Piercing, entry.Attack.Alternative.Type);
        Assert.Equal(AttackDamageCondition.AttackerIsBloodied, entry.Attack.Alternative.Condition);
        // The alternative replaces only the Piercing base (index 0), leaving the Poison.
        Assert.Equal(0, entry.Attack.Alternative.ReplacesComponentIndex);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheMimicEmDashOrPlusChainAndItsHeaderParentheticalBothStructure()
    {
        // Mimic's Bite, verbatim as extracted (#409/#666, SRD 5.2.1 p. 309). The
        // Piercing base alternates on the target being Grappled by the mimic (the
        // attacker), and the Acid is added unconditionally. The attack header carries
        // a separate "(with Advantage if the target is Grappled by the mimic)"
        // parenthetical — before #666 this sat in AttackHeaderPattern's unread filler
        // and stayed honest residue; #666 gives it a structured field
        // (AttackRollAdvantageCondition.TargetIsGrappledByAttacker) read by the same
        // shared IsGrappledBy predicate #409's damage tier already uses, so the two
        // riders — keyed on the identical printed state — can never disagree.
        var entry = EntryMechanicsParser.Classify(
            "Bite",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5 (with Advantage if the target is Grappled by the mimic), reach 5 ft. " +
            "Hit: 7 (1d8 + 3) Piercing damage-or 12 (2d8 + 3) Piercing damage if the target is Grappled " +
            "by the mimic-plus 4 (1d8) Acid damage.");

        Assert.Collection(
            entry.Attack!.Damage,
            piercing =>
            {
                Assert.Equal(DamageType.Piercing, piercing.Type);
                Assert.Equal(7, piercing.PrintedAverage);
                Assert.Null(piercing.Condition);
            },
            acid =>
            {
                Assert.Equal(DamageType.Acid, acid.Type);
                Assert.Equal(4, acid.PrintedAverage);
                Assert.Null(acid.Condition);
            });

        Assert.NotNull(entry.Attack.Alternative);
        Assert.Equal(12, entry.Attack.Alternative!.PrintedAverage);
        Assert.Equal(DamageType.Piercing, entry.Attack.Alternative.Type);
        Assert.Equal(AttackDamageCondition.TargetIsGrappledByAttacker, entry.Attack.Alternative.Condition);
        Assert.Equal(0, entry.Attack.Alternative.ReplacesComponentIndex);

        Assert.Equal(AttackRollAdvantageCondition.TargetIsGrappledByAttacker, entry.Attack.AdvantageCondition);

        // The Mimic's Bite itself is fully modelled now; the entry still carries
        // residue from its Pseudopod (untouched by this slice), so the Mimic as a
        // whole stays Diminished rather than re-entering the pool.
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheAnkhegsBiteStructuresItsGrappledByAttackerAdvantageCircumstance()
    {
        // Ankheg's Bite, verbatim as extracted (#666, SRD 5.2.1 p. 259). No em-dash
        // chain here — just the header parenthetical alongside plain damage — so this
        // pins the simpler of the two shapes independently of the Mimic's combination.
        var entry = EntryMechanicsParser.Classify(
            "Bite",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5 (with Advantage if the target is Grappled by the ankheg), reach 5 ft. " +
            "Hit: 10 (2d6 + 3) Slashing damage plus 3 (1d6) Acid damage.");

        Assert.Equal(AttackRollAdvantageCondition.TargetIsGrappledByAttacker, entry.Attack!.AdvantageCondition);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheGiantSharksBiteStructuresItsMissingHitPointsAdvantageCircumstance()
    {
        // Giant Shark's Bite, verbatim as extracted (#666, SRD 5.2.1 p. 353) — the
        // "doesn't have all its Hit Points" branch, the second of the shape's two
        // circumstances.
        var entry = EntryMechanicsParser.Classify(
            "Bite",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +9 (with Advantage if the target doesn't have all its Hit Points), " +
            "reach 5 ft. Hit: 22 (3d10 + 6) Piercing damage.");

        Assert.Equal(AttackRollAdvantageCondition.TargetIsMissingHitPoints, entry.Attack!.AdvantageCondition);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheSwarmOfPiranhasCombinesItsMissingHitPointsAdvantageWithItsOwnBloodiedTier()
    {
        // Swarm of Piranhas' Bites, verbatim (#666, SRD 5.2.1 p. 362) — the one entry
        // where the header's missing-Hit-Points Advantage circumstance and #371's own
        // "or…if the swarm is Bloodied" alternative damage tier both print on the same
        // attack. The two are independent fields read from independent state (the
        // target's Hit Points for one, the swarm's own for the other) and this pins
        // that structuring one does not disturb the other.
        var entry = EntryMechanicsParser.Classify(
            "Bites",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +5 (with Advantage if the target doesn't have all its Hit Points), " +
            "reach 5 ft. Hit: 8 (2d4 + 3) Piercing damage, or 5 (1d4 + 3) Piercing damage if the swarm " +
            "is Bloodied.");

        Assert.Equal(AttackRollAdvantageCondition.TargetIsMissingHitPoints, entry.Attack!.AdvantageCondition);
        Assert.NotNull(entry.Attack.Alternative);
        Assert.Equal(AttackDamageCondition.AttackerIsBloodied, entry.Attack.Alternative!.Condition);
        Assert.Null(entry.Attack.Alternative.ReplacesComponentIndex);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheDoppelgangersFirstRoundParentheticalIsNotAMatchedShapeAndStaysResidue()
    {
        // Doppelganger's Slam, verbatim (SRD 5.2.1 p. 280 right column) — the ninth
        // attack-header Advantage parenthetical the corpus prints, and deliberately
        // NOT one of #666's two circumstances: "during the first round of each
        // combat" is a predicate over the encounter clock, not over attacker/target
        // state, and AttackRollAdvantageConditionPattern must not widen to reach it.
        var entry = EntryMechanicsParser.Classify(
            "Slam",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +6 (with Advantage during the first round of each combat), reach 5 ft. " +
            "Hit: 11 (2d6 + 4) Bludgeoning damage.");

        Assert.Null(entry.Attack!.AdvantageCondition);
        Assert.Equal(
            ["(with Advantage during the first round of each combat)"],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void AnOrIfAlternativeConditionedOnAChargeIsNotAMatchedShapeAndFallsToResidue()
    {
        // Goat's Ram, verbatim (#371's own issue text). The engine tracks no movement
        // history to check a charge against, so AlternativeDamagePattern does not
        // reach for this shape at all — Attack.Alternative stays null and the whole
        // clause is honest residue, exactly as an unclaimed span always is (design
        // §4.3), rather than a structured condition the engine could never satisfy.
        var entry = EntryMechanicsParser.Classify(
            "Ram",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +2, reach 5 ft. Hit: 1 Bludgeoning damage, or 2 (1d4) Bludgeoning " +
            "damage if the goat moved 20+ feet straight toward the target immediately before the hit.");

        Assert.Null(entry.Attack!.Alternative);
        var damage = Assert.Single(entry.Attack.Damage);
        Assert.Equal(1, damage.PrintedAverage);
        Assert.Equal(
            [
                "or 2 (1d4) Bludgeoning damage if the goat moved 20+ feet straight toward the target " +
                "immediately before the hit",
            ],
            entry.UnmodelledClauses);
    }

    #endregion

    #region Plural conditions (#372)

    [Fact]
    public void APluralConjunctionWhereBothNamesAreExecutableClaimsEdgeToEdge()
    {
        // Rakshasa's Baleful Command, verbatim. Frightened and Incapacitated are both on
        // ConditionRules' executable allowlist, so each name's own claim (its own word,
        // plus the shared lead-in or the shared trailing duration that sits on the far
        // side of its sibling — SplitPluralConditionClaim's own split) meets its
        // neighbour with nothing between them but the bare "and " connective, which
        // ordinary glue absorption closes. Zero residue for the rider clause: a plural
        // conjunction is not, by itself, a reason to demote a monster's grade.
        var entry = EntryMechanicsParser.Classify(
            "Baleful Command",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 18, each enemy in a 30-foot Emanation originating from the " +
            "rakshasa. Failure: 28 (8d6) Psychic damage, and the target has the Frightened and " +
            "Incapacitated conditions until the start of the rakshasa's next turn.");

        Assert.Equal(2, entry.AppliedConditions.Count);

        var frightened = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Frightened);
        Assert.True(frightened.IsFullyModelled);
        Assert.NotNull(frightened.Duration);
        Assert.True(frightened.Duration!.Owner == ConditionDurationOwner.Source);

        var incapacitated = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Incapacitated);
        Assert.True(incapacitated.IsFullyModelled);
        Assert.NotNull(incapacitated.Duration);

        // Zero residue: the whole target clause, "each enemy in a 30-foot Emanation
        // originating from the rakshasa", now claims (#601 — SaveTargetClausePattern's
        // `selector` group reads "enemy" the same way it already read "creature", and
        // Area.EnemiesOnly records the reading). This fixture predates #601 and used
        // to pin that clause as residue; it now pins the plural conjunction alone,
        // unrelated to the area selector.
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void APluralConjunctionWhereTheSecondNameIsInexecutableLeavesItAndItsConnectiveAsResidue()
    {
        // Storm Giant's Thunderbolt, verbatim (#372's own issue text). Blinded is
        // executable and is imposed; Deafened is not on ConditionRules.Executable
        // (deliberately absent for want of a hearing model) and is never imposed —
        // CanBeImposed gates that at runtime exactly as it always has, unchanged by
        // this fix. What changes is that Deafened is now recognised at all: both names
        // reach AppliedConditions (the model expresses the printed name and duration
        // for each), and only Deafened's own word plus its bordering "and" — the text
        // Blinded's claim does not reach into — is left as residue, rather than the
        // whole clause vanishing into nothing the way it did before this fix.
        var entry = EntryMechanicsParser.Classify(
            "Thunderbolt",
            MonsterEntrySection.Action,
            "Ranged Attack Roll: +14, range 500 ft. Hit: 22 (2d12 + 9) Lightning damage, and the " +
            "target has the Blinded and Deafened conditions until the start of the giant's next " +
            "turn.");

        Assert.Equal(2, entry.AppliedConditions.Count);

        var blinded = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Blinded);
        Assert.True(blinded.IsFullyModelled);
        Assert.NotNull(blinded.Duration);

        var deafened = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Deafened);
        Assert.True(deafened.IsFullyModelled);
        Assert.NotNull(deafened.Duration);

        Assert.Equal(["and Deafened"], entry.UnmodelledClauses);
    }

    [Fact]
    public void APluralConjunctionWhereTheFirstNameIsInexecutableLeavesItAndItsConnectiveAsResidue()
    {
        // Tarrasque's Thunderous Bellow, verbatim. The mirror of the Storm Giant case
        // above: Deafened is the FIRST printed name here, not the second, and the split
        // still isolates exactly its own word plus its bordering "and" — "Deafened and"
        // rather than "and Deafened" — proving the split reads the match's own group
        // positions rather than assuming which side an inexecutable name falls on. The
        // other residue line is pre-existing and unrelated to this fix: the target
        // clause's distance/count qualifier (design §7.6, claimed only up to "in a"
        // before the area). The trailing "only" off "Success: Half damage only." used
        // to strand as its own residue line here too; #397 widened
        // `save.success_half`'s claim to cover it, since the engine already skips
        // every rider on a `HalfDamage` success without ever reading that word.
        var entry = EntryMechanicsParser.Classify(
            "Thunderous Bellow",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 27, each creature and each object that isn't being worn " +
            "or carried in a 150-foot Cone. Failure: 78 (12d12) Thunder damage, and the target has " +
            "the Deafened and Frightened conditions until the end of its next turn. Success: Half " +
            "damage only.");

        Assert.Equal(2, entry.AppliedConditions.Count);

        var deafened = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Deafened);
        Assert.True(deafened.IsFullyModelled);

        var frightened = Assert.Single(entry.AppliedConditions, c => c.Condition == ConditionType.Frightened);
        Assert.True(frightened.IsFullyModelled);
        Assert.True(frightened.Duration!.Owner == ConditionDurationOwner.Bearer);

        Assert.Equal(
            [
                "each creature and each object that isn't being worn or carried in a",
                "Deafened and",
            ],
            entry.UnmodelledClauses);
    }

    #endregion

    #region Polarity guard (#407)

    [Fact]
    public void ImmunityToAConditionIsResidueNotARefusedApplication()
    {
        // Mindless Rage, verbatim. Before #407 this read "Immunity to the Charmed and
        // Frightened conditions" as if Mindless Rage inflicted Charmed and Frightened
        // on someone, recording both as a refused AppliedCondition — backwards, since
        // the printed sentence grants immunity to them. Neither name reaches
        // AppliedConditions at all now; both stay exactly where the rest of this
        // Unmodelled trait's prose already lives.
        var trait = EntryMechanicsParser.ClassifyTrait(
            "Mindless Rage",
            "You have Immunity to the Charmed and Frightened conditions while your Rage is " +
            "active. If you're Charmed or Frightened when you enter your Rage, the condition " +
            "ends on you.");

        Assert.Equal(EntryMechanics.Unmodelled, trait.Mechanics);
        Assert.Empty(trait.AppliedConditions);
        Assert.Contains(
            trait.UnmodelledClauses,
            clause => clause.Contains(
                "Immunity to the Charmed and Frightened conditions",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NoLongerHasAConditionIsResidueNotARefusedApplication()
    {
        // The Purple Worm's Swallow carries this exact sentence, but in the full stat
        // block Restrained is already recorded (refused for an unrelated reason) from
        // an earlier clause in the same entry, and the existing-condition dedup a few
        // lines above this guard hides the removal sentence regardless of polarity.
        // Isolating just the removal sentence here proves the guard itself keeps it
        // out, rather than the dedup that happens to also hide it in the real entry.
        var entry = EntryMechanicsParser.Classify(
            "Test Removal",
            MonsterEntrySection.Trait,
            "If the worm dies, any swallowed creature no longer has the Restrained " +
            "condition and can escape from the corpse using 20 feet of movement, exiting " +
            "Prone.");

        Assert.Equal(EntryMechanics.Unmodelled, entry.Mechanics);
        Assert.Empty(entry.AppliedConditions);
        Assert.Contains(
            entry.UnmodelledClauses,
            clause => clause.Contains("no longer has the Restrained condition", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("You have Immunity to")]
    [InlineData("It no longer has")]
    [InlineData("Creatures can't gain")]
    [InlineData("Creatures cannot gain")]
    [InlineData("The target can't be")]
    [InlineData("The target cannot be")]
    public void EveryNegatedLeadInKeepsTheConditionOutOfAppliedConditions(string leadIn)
    {
        // Hallow's "can't gain the Frightened condition" is the corpus's own instance
        // of the third phrase; "can't be" has no directly-adjacent printing today (see
        // NegatedConditionLeadInPattern's own remarks) but is pinned here per #407's
        // acceptance criteria, both contracted and spelled out.
        var trait = EntryMechanicsParser.ClassifyTrait(
            "Test Trait",
            $"{leadIn} the Frightened condition while this effect lasts.");

        Assert.Empty(trait.AppliedConditions);
    }

    [Fact]
    public void AvoidOrEndLeadInIsUnaffectedAndStillRecordedAsARefusedApplication()
    {
        // Dwarven Resilience, verbatim. "Advantage on saving throws ... to avoid or
        // end the Poisoned condition" is a different clause shape from #407's own
        // three — it is not a claim that the bearer currently has, lacks, or is
        // immune to the condition — and widening the guard to catch it is explicitly
        // out of #407's scope. This still lands as a refused AppliedCondition,
        // unchanged by this fix, as a trip-wire against the guard over-matching.
        var trait = EntryMechanicsParser.ClassifyTrait(
            "Dwarven Resilience",
            "You have Resistance to Poison damage. You also have Advantage on saving " +
            "throws you make to avoid or end the Poisoned condition.");

        var rider = Assert.Single(trait.AppliedConditions);
        Assert.Equal(ConditionType.Poisoned, rider.Condition);
        Assert.False(rider.IsFullyModelled);
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

    #region Post-hit debuff riders (#665, shape 3 of the #390 ledger)

    [Fact]
    public void TheEttinsMorningstarStructuresTheBearerClockDisadvantageRider()
    {
        var entry = EntryMechanicsParser.Classify(
            "Morningstar",
            MonsterEntrySection.Action,
            "Melee Attack Roll: +7, reach 5 ft. Hit: 14 (2d8 + 5) Piercing damage, and the " +
            "target has Disadvantage on the next attack roll it makes before the end of its " +
            "next turn.");

        Assert.Equal(EntryMechanics.Attack, entry.Mechanics);
        Assert.True(entry.Attack!.ImposesDisadvantageOnTargetsNextAttack);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void TheFireGiantsRockClaimsOnlyTheDisadvantageClauseLeavingThePushAsResidue()
    {
        // Verbatim shape from the corpus: the same Disadvantage clause trails a
        // second, unrelated printed effect (a forced push) this engine does not
        // execute (WeaponMasteryRules' own Push reasoning applies here too). The
        // parser must claim only the Disadvantage clause and leave the push
        // unclaimed — not swallow the whole sentence, and not refuse the whole
        // sentence either.
        var entry = EntryMechanicsParser.Classify(
            "Hammer Throw",
            MonsterEntrySection.Action,
            "Ranged Attack Roll: +9, range 150/600 ft. Hit: 23 (3d10 + 7) Bludgeoning damage " +
            "plus 4 (1d8) Fire damage, and the target is pushed up to 15 feet straight away " +
            "from the giant and has Disadvantage on the next attack roll it makes before the " +
            "end of its next turn.");

        Assert.True(entry.Attack!.ImposesDisadvantageOnTargetsNextAttack);
        Assert.Equal(
            ["and the target is pushed up to 15 feet straight away from the giant"],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void AnUnnamedNextTurnOnASavingThrowIsNotClaimedAsTheSourcesClock()
    {
        // The trip-wire the shape-3 reading rests on (#665): only a *named* imposer
        // ("the mephit's next turn") reads as the source's clock. An unnamed "its
        // next turn" printing of the same Speed-decrease rider — seen elsewhere in
        // the corpus, always on a plain Attack entry rather than a save — is the
        // bearer's clock instead, a different shape this parser does not attempt to
        // read as the source's; if a future save-based entry ever prints this
        // unnamed form, it must fall to residue rather than being silently claimed
        // under the wrong clock.
        var entry = EntryMechanicsParser.Classify(
            "Test Breath",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 10, one creature. Failure: 5 (2d4) Fire damage, " +
            "and the target's Speed decreases by 10 feet until the end of its next turn.");

        Assert.Null(entry.Save!.TargetSpeedDecreaseFeet);
        Assert.Contains(
            "and the target's Speed decreases by 10 feet until the end of its next turn",
            entry.UnmodelledClauses);
    }

    [Fact]
    public void TheSteamMephitsSteamBreathStructuresTheNamedSourceClockSpeedDecrease()
    {
        var entry = EntryMechanicsParser.Classify(
            "Steam Breath",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 10, each creature in a 15-foot Cone. Failure: 5 " +
            "(2d4) Fire damage, and the target's Speed decreases by 10 feet until the end of " +
            "the mephit's next turn. Success: Half damage only.");

        Assert.Equal(10, entry.Save!.TargetSpeedDecreaseFeet);
        Assert.Empty(entry.UnmodelledClauses);
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

    [Fact]
    public void AKillAndHealRiderBehindAFailureLabelIsResidueNotSilentlyDropped()
    {
        // Will-o'-Wisp's Consume Life, verbatim (#373). "The target dies, and the wisp
        // regains 10 (3d6) Hit Points" is real, unexecuted mechanics — a kill-and-heal
        // rider with no ConditionType to attach to at all, so it never reaches
        // AppliedConditions the way the Balor's Prone rider above does; it is counted
        // straight into UnmodelledClauses instead. This entry's other two residue
        // lines are a different gap: a single-target save entry claims only the head
        // noun "one creature" (design §7.6) plus, since #386, the printed range —
        // "within 5 feet" is carved out of the qualifier below and structured onto
        // RangeFeet — but the sight qualifier and the "that has 0 Hit Points" gate
        // are printed rules UseSaveEntry does not enforce, so what's left of the
        // qualifier splits into two honest residue chunks around the claimed range.
        var entry = EntryMechanicsParser.Classify(
            "Consume Life",
            MonsterEntrySection.BonusAction,
            "Constitution Saving Throw: DC 10, one living creature the wisp can see within 5 " +
            "feet that has 0 Hit Points. Failure: The target dies, and the wisp regains 10 (3d6) " +
            "Hit Points.");

        Assert.Equal(5, entry.Save!.RangeFeet);
        Assert.Equal(
            [
                "one living creature the wisp can see",
                "that has 0 Hit Points",
                "The target dies, and the wisp regains 10 (3d6) Hit Points",
            ],
            entry.UnmodelledClauses);
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
    public void TheBeardedDevilRecordsAnExactPerNameComposition()
    {
        // Issue #343 — STRICT-EXACT reading: every clause names exactly one attack and
        // the two names are distinct, so this is a printed enumerated composition, not
        // a free choice. Before #343 two named attacks summed to `AnyCombination:
        // true`, wrongly letting the tactics policy swing two Beards instead of one
        // Beard and one Infernal Glaive.
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The devil makes one Beard attack and one Infernal Glaive attack.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(2, entry.Multiattack!.AttackCount);
        Assert.Equal(["Beard", "Infernal Glaive"], entry.Multiattack.AttackNames);
        Assert.False(entry.Multiattack.AnyCombination);
        Assert.Equal(
            [new MultiattackComponent("Beard", 1), new MultiattackComponent("Infernal Glaive", 1)],
            entry.Multiattack.Composition);
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
    public void AnAlternativeCompositionFollowedByASecondSentenceIsNeitherDuplicatedNorSwallowed()
    {
        // #359 (qc's review of #356): AlternativeCompositionPattern's own match extends
        // to end-of-string via ".*$" (Singleline) — but that extent is never read, only
        // the match's Index, which truncates `text` to just the first branch. The
        // discarded suffix (the alternative branch, here followed by a further
        // sentence, one sentence past anything in the corpus today) is never claimed
        // against any coverage, so it cannot be duplicated by a separate hand-back —
        // there isn't one — and EntryCoverage.Residue() chunks it at sentence
        // boundaries regardless of the regex's own reach, exactly the same structural
        // guarantee #360 pinned for the (now-deleted) bundled-use scan. This test pins
        // both properties directly: a regression in either would merge the two
        // trailing sentences into one clause or drop one of them.
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The golem makes two Slam attacks, or it makes three Slam attacks if it used Hasten " +
            "this turn. It can replace one attack with a use of Ground Slam if available.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(2, entry.Multiattack!.AttackCount);
        Assert.Equal(["Slam"], entry.Multiattack.AttackNames);

        // Exactly two clauses — the alternative branch recorded once (not folded
        // together with, or duplicated against, the second sentence).
        Assert.Equal(
            [
                "or it makes three Slam attacks if it used Hasten this turn",
                "It can replace one attack with a use of Ground Slam if available",
            ],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void ANamedSubjectAlternativeCompositionIsAStatedParserLimitCaughtByTheValidatorInstead()
    {
        // #359, tightened after qc's Medium on this PR's first attempt: widening
        // AlternativeCompositionPattern's subject to any "the <word>" was both too
        // broad (a hypothetical "or the target makes ... attacks" printed for an
        // unrelated creature would have been sliced off as if it were this creature's
        // own alternative, silently truncating what the composition reads as — a real
        // extraction change, not just a flag) and too narrow (a multiword or hyphenated
        // repeated name, "the clay golem", still would not have matched a bare \w+).
        // The parser stays conservative — pronoun subjects only — on purpose: a false
        // match here changes AttackCount, so the cost of a miss must fall on a human
        // reviewer instead. This test pins the *current, honest* failure mode: a
        // named-subject alternative is not recognised as a second composition at all,
        // so its clause is summed into AttackCount (5, not the intended 2) and its
        // residue is left fragmented rather than surviving as the one intact clause a
        // recognised alternative leaves (contrast
        // AnAlternativeCompositionFollowedByASecondSentenceIsNeitherDuplicatedNorSwallowed
        // and the Clay Golem test above). That fragmentation — "makes three Slam
        // attacks" itself fully absorbed, leaving only the subject and the trailing
        // qualifier as separate scraps — is exactly the signal
        // MonsterValidator.AlternativeCompositionMarker now checks for (ValidatorTests'
        // ANamedSubjectAlternativeCompositionThatParserFragmentsUnclaimed_IsAnError):
        // the validator's independent, broader "makes ... attacks" scan flags this
        // shape because no single residue clause reproduces it intact, catching what
        // the deliberately-conservative parser misses instead of the parser
        // mis-truncating to catch it.
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The golem makes two Slam attacks, or the golem makes three Slam attacks if it used " +
            "Hasten this turn.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(5, entry.Multiattack!.AttackCount);
        Assert.Equal(["Slam"], entry.Multiattack.AttackNames);
        Assert.Equal(["or the golem", "if it used Hasten this turn"], entry.UnmodelledClauses);
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
        // Issue #343 — both clauses name exactly one attack each, so this is a printed
        // enumerated composition (two Tentacle, two Bite) rather than a free choice.
        Assert.False(entry.Multiattack.AnyCombination);
        Assert.Equal(
            [new MultiattackComponent("Tentacle", 2), new MultiattackComponent("Bite", 2)],
            entry.Multiattack.Composition);

        // #382 deleted BundledMultiattackUseClauses (design §7.4): nothing claims a
        // "uses"/"can use" clause, so it lands in residue by subtraction instead of a
        // synthesised, capitalized hand-back. AdjacentMakesPattern's claim is only the
        // word "makes" itself (design §7.4's own description — "adjacency is judged
        // modulo glue" — is about the adjacency test, not a claim over the glue), so
        // the bridging "and" stays in the same uncovered run as "uses Reel," rather
        // than being absorbed: the run carries real words ("uses", "Reel"), which
        // fails the glue-only test before the trailing-connective question is ever
        // asked (design §4.2, rule 1), and TrimGlue deliberately never trims a
        // trailing connective word (§4.2, rule 3) — a dangling "and" is the model
        // saying a fragment was lost here, not a formatting slip. The string is
        // verbatim from the entry's own text (§6.2), lowercase "uses" included.
        Assert.Equal(["uses Reel, and"], entry.UnmodelledClauses);
    }

    [Fact]
    public void AThreeNameCompositionRecordsAllThreeComponentsInPrintedOrder()
    {
        // Chimera's Multiattack (the substitution sentence that follows in print,
        // "It can replace the Claw attack with a use of Fire Breath if available.", is
        // a separate sentence and out of scope here — ParseMultiattack only reads the
        // first).
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The chimera makes one Ram attack, one Bite attack, and one Claw attack.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(3, entry.Multiattack!.AttackCount);
        Assert.Equal(["Ram", "Bite", "Claw"], entry.Multiattack.AttackNames);
        Assert.False(entry.Multiattack.AnyCombination);
        Assert.Equal(
            [
                new MultiattackComponent("Ram", 1),
                new MultiattackComponent("Bite", 1),
                new MultiattackComponent("Claw", 1),
            ],
            entry.Multiattack.Composition);
    }

    [Fact]
    public void AMixedFixedAndChoiceCompositionIsNotClaimedAsEitherShape()
    {
        // Design §2.5's defensive guard for issue #343: no monster in the corpus mixes
        // a single-named clause with a choice clause today (the Tarrasque's matching
        // hybrid already exits earlier, via `total < 2`), but the model has no shape
        // for a fixed part plus a free part and must not silently claim one anyway —
        // neither a composition (which would drop the free choice) nor
        // `AnyCombination: true` (which would drop the fixed cap). The whole sentence
        // falls to Unmodelled instead, honestly.
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The chimera makes one Ram attack and two Claw or Bite attacks.");

        Assert.Equal(EntryMechanics.Unmodelled, entry.Mechanics);
        Assert.Null(entry.Multiattack);
        Assert.Equal(
            ["The chimera makes one Ram attack and two Claw or Bite attacks."],
            entry.UnmodelledClauses);
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

    [Fact]
    public void ABundledUseFollowedByASecondSentenceIsNeitherDuplicatedNorSwallowed()
    {
        // #360: the deleted (#382) BundledMultiattackUseClauses used to scan the whole
        // entry text with a lazy capture ending at "the next composition clause or the
        // sentence's end" — but "the sentence's end" was written as `\.\s*$|$`, an
        // end-of-*string* anchor, not an end-of-*sentence* one. On a hypothetical
        // multi-sentence entry folding a bundled use into its first sentence — the shape
        // this test constructs, one sentence past what any of the fourteen corpus
        // entries #341/#358 fixed actually prints — that old scan would have swallowed
        // the second sentence into the bundled-use fragment, and (since every affected
        // entry was one sentence when #341/#358 shipped) duplicated it against whatever
        // else already recorded that second sentence as unmodelled.
        //
        // Coverage-by-consumption (#382) forecloses both failure modes structurally
        // rather than by a sharper regex: nothing scans for a bundled-use clause at all
        // any more, so there is no separate fragment to duplicate against residue), and
        // `EntryCoverage.Residue()` chunks every surviving uncovered run at sentence
        // boundaries (`ChunkAtSentenceBoundaries`) before it is ever reported, so a run
        // spanning two sentences always yields two clauses, never one merged blob. This
        // test pins both properties directly against a text shaped like #360's own
        // hypothetical, so a regression in either guarantee turns it red.
        var entry = EntryMechanicsParser.Classify(
            "Multiattack",
            MonsterEntrySection.Action,
            "The lich makes two Chill Touch attacks and uses Life Drain. It can replace one " +
            "attack with a use of Frost Bolt.");

        Assert.NotNull(entry.Multiattack);
        Assert.Equal(2, entry.Multiattack!.AttackCount);
        Assert.Equal(["Chill Touch"], entry.Multiattack.AttackNames);

        // Exactly two clauses — the bundled use recorded once (not folded together with,
        // or duplicated against, the second sentence).
        Assert.Equal(
            ["and uses Life Drain", "It can replace one attack with a use of Frost Bolt"],
            entry.UnmodelledClauses);
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

    [Fact]
    public void ATrailingFailureOrSuccessSideClauseDoesNotOverrideAPrintedSuccessHalfDamage()
    {
        // Steam Mephit's Steam Breath, verbatim (#370). "Failure or Success: Being
        // underwater doesn't grant Resistance to this Fire damage." is a side clause
        // about Resistance, not a restatement of the Failure damage, so it must not
        // override the printed "Success: Half damage only." into SameAsFailure — a
        // successful save halves the 2d4 Fire damage, exactly as printed, rather than
        // taking it in full. The side clause itself is unexecuted mechanics (out of
        // #665's scope — a Resistance carve-out unrelated to the Speed rider) and
        // lands in residue.
        //
        // The trailing "only" used to strand as its own residue line here (filed as
        // #397): `save.success_half` claimed exactly the literal "Success: Half
        // damage", so " only." was left unclaimed even though it documents behaviour
        // the engine already has — `Encounter.cs`'s rider application skips every
        // rider on a `HalfDamage` success regardless of whether "only" was read.
        // #397 widened the claim to cover it; it is absent from residue here now.
        //
        // The rider ("Speed decreases by 10 feet ... the mephit's next turn") used to
        // strand as residue too, until #665 (shape 3 of the #390 ledger) structured
        // it: the imposer is named, so it is the source's own clock, and it now
        // executes through `SaveEffect.TargetSpeedDecreaseFeet`.
        var entry = EntryMechanicsParser.Classify(
            "Steam Breath",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 10, each creature in a 15-foot Cone. Failure: 5 (2d4) " +
            "Fire damage, and the target's Speed decreases by 10 feet until the end of the " +
            "mephit's next turn. Success: Half damage only. Failure or Success: Being " +
            "underwater doesn't grant Resistance to this Fire damage.");

        Assert.Equal(SaveSuccessOutcome.HalfDamage, entry.Save!.SuccessOutcome);
        Assert.Equal(10, entry.Save!.TargetSpeedDecreaseFeet);
        Assert.Equal(
            [
                "Failure or Success: Being underwater doesn't grant Resistance to this Fire damage",
            ],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void ATrailingFailureOrSuccessSideClauseWithNoSuccessLineParsesToNoEffect()
    {
        // Adult Black Dragon's Cloud of Insects, verbatim (#370). No "Success:" clause
        // is printed at all — the SRD default reading is that the effect is avoided
        // entirely — so the trailing "Failure or Success: The dragon can't take this
        // action again until the start of its next turn." recharge clause must not
        // read as SameAsFailure and force full damage through on a success.
        var entry = EntryMechanicsParser.Classify(
            "Cloud of Insects",
            MonsterEntrySection.LegendaryAction,
            "Dexterity Saving Throw: DC 17, one creature the dragon can see within 120 feet. " +
            "Failure: 22 (4d10) Poison damage, and the target has Disadvantage on saving throws " +
            "to maintain Concentration until the end of its next turn. Failure or Success: The " +
            "dragon can't take this action again until the start of its next turn.");

        Assert.Equal(SaveSuccessOutcome.NoEffect, entry.Save!.SuccessOutcome);
        Assert.Contains(
            "Failure or Success: The dragon can't take this action again until the start of its next turn",
            entry.UnmodelledClauses);
    }

    #endregion

    #region Section-gated riders (#373)

    [Fact]
    public void ATraitSectionSavingThrowsRiderIsResidueBecauseUseEntryNeverFiresATrait()
    {
        // Hezrou's Stench, verbatim. Encounter.UseEntry refuses any entry whose section
        // isn't Action or BonusAction before it ever reads Mechanics
        // (entry.not_an_action), so nothing ever imposes this Poisoned rider even
        // though Poisoned is on ConditionRules.Executable and the rider itself reads
        // clean (a bare "Failure: The target has the Poisoned condition until the
        // start of its next turn." with no gate, no trailing text). Claiming its span
        // anyway would be the exact false claim design §2.5 forbids for Multiattack,
        // now closed for every section rather than kept as a five-entry exception.
        var entry = EntryMechanicsParser.Classify(
            "Stench",
            MonsterEntrySection.Trait,
            "Constitution Saving Throw: DC 16, any creature that starts its turn in a 10-foot " +
            "Emanation originating from the hezrou. Failure: The target has the Poisoned " +
            "condition until the start of its next turn.");

        Assert.Equal(EntryMechanics.SavingThrow, entry.Mechanics);

        // The condition is still structurally recorded — the model does express what
        // the rider and its duration would be, the same treatment an unimposable
        // condition already gets (design §2.5's surviving half) — only its claim is
        // withheld, not its presence in AppliedConditions.
        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Poisoned, rider.Condition);

        // The other two lines are pre-existing and unrelated to the section gate: the
        // target clause's Emanation qualifier (design §7.6 claims only the area shape
        // and its origin word, "originating from"; the free-standing lead-in "any
        // creature that starts its turn in a" is not one of the claimed shapes).
        Assert.Equal(
            [
                "any creature that starts its turn in a",
                "originating from the hezrou",
                "The target has the Poisoned condition until the start of its next turn",
            ],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void ANActionSectionSavingThrowWithTheIdenticalRiderShapeStillClaimsIt()
    {
        // The same rider grammar as the Trait fixture above, verbatim except for its
        // section — proving the gate reads section, not the rider's own shape. An
        // Action-section entry is exactly what Encounter.UseEntry does dispatch, so
        // the rider claims and the entry is fully modelled.
        var entry = EntryMechanicsParser.Classify(
            "Stench",
            MonsterEntrySection.Action,
            "Constitution Saving Throw: DC 16, any creature that starts its turn in a 10-foot " +
            "Emanation originating from the hezrou. Failure: The target has the Poisoned " +
            "condition until the start of its next turn.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Poisoned, rider.Condition);
        Assert.True(rider.IsFullyModelled);

        Assert.DoesNotContain(
            "The target has the Poisoned condition until the start of its next turn",
            entry.UnmodelledClauses);
    }

    [Fact]
    public void ALegendaryActionSavingThrowsRiderIsAlsoResidueNotOnlyTraits()
    {
        // Solar's Blinding Gaze, verbatim. #373's issue text names Trait-section
        // entries by name, but Encounter.UseEntry refuses LegendaryAction and
        // Reaction identically to Trait — none of the three is Action or
        // BonusAction — so the gate is section-general, not Trait-specific, and this
        // pins that the fix actually reads that way rather than only the named case.
        var entry = EntryMechanicsParser.Classify(
            "Blinding Gaze",
            MonsterEntrySection.LegendaryAction,
            "Constitution Saving Throw: DC 25, one creature the solar can see within 120 feet. " +
            "Failure: The target has the Blinded condition for 1 minute. Failure or Success: " +
            "The solar can't take this action again until the start of its next turn.");

        var rider = Assert.Single(entry.AppliedConditions);
        Assert.Equal(ConditionType.Blinded, rider.Condition);

        // The other two lines are pre-existing and unrelated to the section gate: the
        // single-target save's sight qualifier (design §7.6 claims only "one
        // creature"; the printed range is now claimed too — #386 — so only "the solar
        // can see" is left of the qualifier, distance carved out) and the recharge
        // clause's own "Failure or Success:" side clause (#370 — claimed by nobody,
        // design §4.1).
        Assert.Equal(120, entry.Save!.RangeFeet);
        Assert.Equal(
            [
                "the solar can see",
                "The target has the Blinded condition for 1 minute",
                "Failure or Success: The solar can't take this action again until the start of its next turn",
            ],
            entry.UnmodelledClauses);
    }

    #endregion

    #region Save range (#386)

    [Fact]
    public void ASingleTargetSavesPrintedRangeStructuresOntoRangeFeet()
    {
        // Mummy's Dreadful Glare, verbatim. "one creature the mummy can see within 60
        // feet" — the sight-before-distance word order. ReadRange finds "within 60
        // feet" wherever it sits and claims exactly that substring, leaving the sight
        // qualifier ("the mummy can see") as its own residue rather than folding the
        // now-claimed distance into it.
        var entry = EntryMechanicsParser.Classify(
            "Dreadful Glare",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 11, one creature the mummy can see within 60 feet. Failure: " +
            "The target has the Frightened condition until the end of the mummy's next turn. " +
            "Success: The target is immune to this mummy's Dreadful Glare for 24 hours.");

        Assert.Equal(60, entry.Save!.RangeFeet);
        Assert.Equal(
            [
                "the mummy can see",
                "Success: The target is immune to this mummy's Dreadful Glare for 24 hours",
            ],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void APointAimedSpheresPrintedRangeStructuresOntoRangeFeet()
    {
        // Adult Green Dragon's Noxious Miasma, verbatim (p.294). A point-aimed
        // Sphere's range is only honest to claim once ParseArea actually structures
        // the Sphere itself. AreaPattern now reads "N-foot-radius Sphere" (#420, the
        // same "-radius" branch SpellEffectParser's own AreaPattern already carried),
        // so Area structures for every one of the corpus's 9 radius-Sphere entries,
        // and ReadRange's point-aimed-Sphere gate — added by #386/#421 specifically
        // to avoid claiming a range without a structured area (see ReadRange's own
        // remarks) — now opens: "within 90 feet" claims onto RangeFeet exactly as
        // the Mummy's single-target "within 60 feet" does above, leaving only the
        // sight qualifier ("the dragon can see") as residue.
        var entry = EntryMechanicsParser.Classify(
            "Noxious Miasma",
            MonsterEntrySection.LegendaryAction,
            "Constitution Saving Throw: DC 17, each creature in a 20-foot-radius Sphere " +
            "centered on a point the dragon can see within 90 feet. Failure: 7 (2d6) Poison " +
            "damage, and the target takes a -2 penalty to AC until the end of its next turn. " +
            "Failure or Success: The dragon can't take this action again until the start of its " +
            "next turn.");

        // #600: asserted before the dereference below, not after, so a future
        // regression that leaves Area null reports what broke instead of an
        // unmessaged NullReferenceException.
        Assert.NotNull(entry.Save!.Area);
        Assert.Equal(AreaShape.Sphere, entry.Save.Area!.Shape);
        Assert.Equal(20, entry.Save.Area.SizeFeet);
        Assert.Null(entry.Save.Area.WidthFeet);
        Assert.Equal(90, entry.Save.RangeFeet);
        Assert.Equal(
            [
                "the dragon can see",
                "and the target takes a -2 penalty to AC until the end of its next turn",
                "Failure or Success: The dragon can't take this action again until the start of its next turn",
            ],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void ASelfOriginatingConesPrintedSizeNeverStructuresOntoRangeFeet()
    {
        // Adult Gold Dragon's Fire Breath, verbatim (p.291). A Cone has no separate
        // "range" — its only printed distance is its own size, already on
        // EffectArea.SizeFeet — and the corpus never prints "within" inside a
        // Cone/Line/Emanation target clause (verified directly against the whole
        // corpus), so ReadRange does not even look: RangeFeet stays null for every
        // self-originating area, always.
        var entry = EntryMechanicsParser.Classify(
            "Fire Breath",
            MonsterEntrySection.Action,
            "Dexterity Saving Throw: DC 21, each creature in a 60-foot Cone. Failure: 66 " +
            "(12d10) Fire damage. Success: Half damage.");

        Assert.Null(entry.Save!.RangeFeet);
        Assert.Equal(SaveSuccessOutcome.HalfDamage, entry.Save.SuccessOutcome);
        Assert.Empty(entry.UnmodelledClauses);
    }

    [Fact]
    public void APointAimedCylinderIsNotAModelledShapeSoItsPrintedRangeStaysResidueToo()
    {
        // Storm Giant's Lightning Storm, verbatim (p.331). AreaTargeting.CanResolve
        // refuses Cylinder outright — no height model — so UseSaveEntry never reaches
        // a range check for this entry regardless of what ReadRange might find;
        // claiming "within 500 feet" here would assert the model enforces a distance
        // nothing will ever measure. ReadRange's own shape gate (single-target or
        // Sphere only) excludes it by construction, not by an incidental non-match.
        var entry = EntryMechanicsParser.Classify(
            "Lightning Storm",
            MonsterEntrySection.Action,
            "Dexterity Saving Throw: DC 18, each creature in a 10-foot-radius, 40-foot-high " +
            "Cylinder originating from a point the giant can see within 500 feet. Failure: 55 " +
            "(10d10) Lightning damage. Success: Half damage.");

        Assert.Null(entry.Save!.RangeFeet);
        Assert.Equal(
            [
                "each creature in a 10-foot-radius, 40-foot-high Cylinder originating from a point " +
                "the giant can see within 500 feet",
            ],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void ABoulderTossesPreambleRangeStructuresOntoRangeFeetDespiteSittingAheadOfTheHeader()
    {
        // Giant Ape's Boulder Toss, verbatim (p.349, #422). Both of ReadRange's
        // existing word orders sit after the save header; this one prints "within
        // 90 feet" in the entry's own preamble sentence, a whole clause ahead of
        // "Dexterity Saving Throw", because the Sphere that follows the header
        // refers back to that point ("centered on that point") rather than naming
        // a fresh one ("centered on a point ... within N feet") the way every other
        // corpus Sphere does. ReadRange now recognises that back-reference and
        // scans the preamble for the range instead, claiming "within 90 feet"
        // there and leaving the hurl/sight clause that surrounds it — and the
        // Sphere's own still-open residue (#420) — exactly as unclaimed as before.
        //
        // The trailing "only" from "Success: Half damage only." is not residue
        // here (#397/#608's SaveSuccessHalfPattern claims it as save.success_half,
        // same as the other nine entries printing that fuller form) — this fixture
        // originally pinned "only" as leftover before that fix existed; #608 didn't
        // know to update it because it landed after this test was written. Merging
        // #606 and #608 together (#343) is what surfaces the skew.
        var entry = EntryMechanicsParser.Classify(
            "Boulder Toss",
            MonsterEntrySection.Action,
            "The ape hurls a boulder at a point it can see within 90 feet. Dexterity Saving " +
            "Throw: DC 17, each creature in a 5-foot-radius Sphere centered on that point. " +
            "Failure: 24 (7d6) Bludgeoning damage. If the target is a Large or smaller " +
            "creature, it has the Prone condition. Success: Half damage only.");

        // #600: asserted before the dereference below, not after — see the same
        // note on APointAimedSpheresPrintedRangeStructuresOntoRangeFeet above.
        Assert.NotNull(entry.Save!.Area);
        Assert.Equal(AreaShape.Sphere, entry.Save.Area!.Shape);
        Assert.Equal(5, entry.Save.Area.SizeFeet);
        Assert.Equal(90, entry.Save.RangeFeet);
        Assert.Equal(
            [
                "The ape hurls a boulder at a point it can see",
                "each creature in a",
                "centered on that point",
            ],
            entry.UnmodelledClauses);
    }

    [Fact]
    public void ADestinationSpaceAheadOfTheHeaderIsNotMistakenForABackReferencedPoint()
    {
        // Bulette's Deadly Leap, verbatim. Its preamble also prints a "within N
        // feet" ahead of the header — "jump to a space within 15 feet that
        // contains ..." — but that distance is the bulette's own leap, not a
        // range the save's target clause ever measures from: the clause after the
        // header is "each creature in the bulette's destination space", which
        // matches neither ReadRange's single-target nor point-aimed-Sphere gate
        // (and certainly not Boulder Toss's literal "centered on that point"
        // back-reference), so RangeFeet must stay null. This pins the boundary
        // #422's fix draws: gating the preamble scan on the literal
        // back-reference, not on "any preamble text containing within N feet",
        // is what keeps this entry's leap distance from being claimed as the
        // save's own range — see ReadRange's remarks for the full accounting of
        // why a broader scan was rejected.
        var entry = EntryMechanicsParser.Classify(
            "Deadly Leap",
            MonsterEntrySection.Action,
            "The bulette spends 5 feet of movement to jump to a space within 15 feet that " +
            "contains one or more Large or smaller creatures. Dexterity Saving Throw: DC 15, " +
            "each creature in the bulette's destination space. Failure: 19 (3d12) Bludgeoning " +
            "damage, and the target has the Prone condition. Success: Half damage, and the " +
            "target is pushed 5 feet straight away from the bulette.");

        Assert.Null(entry.Save!.RangeFeet);
        Assert.Equal(
            [
                "The bulette spends 5 feet of movement to jump to a space within 15 feet that " +
                    "contains one or more Large or smaller creatures",
                "each creature in the bulette's destination space",
                "and the target has the Prone condition",
                "and the target is pushed 5 feet straight away from the bulette",
            ],
            entry.UnmodelledClauses);
    }

    #endregion

    #region Enemies-only selector (#601)

    [Fact]
    public void EachEnemyInASphereStructuresAnEnemiesOnlyArea()
    {
        // Planetar's Holy Burst, verbatim (p.315). The corpus's overwhelmingly common
        // wording is "each creature in a ... Sphere" (AFullSaveHeaderParsesAbilityDcAreaAndDamage
        // et al. above), reaching whoever the geometry catches; this instead prints
        // "each enemy", narrowing that same Sphere to creatures hostile to the
        // planetar. SaveTargetClausePattern's `selector` group reads that word and
        // ParseSave folds it onto Area.EnemiesOnly (#601) — see that field's own doc
        // comment and AreaTargeting's remarks for where the narrowing is actually
        // applied (Encounter.SaveVictims, not here: this is a parsing-level claim,
        // not an engine test). The sight qualifier stays residue exactly as it does
        // for every other point-aimed Sphere (APointAimedSpheresPrintedRangeStructuresOntoRangeFeet).
        var entry = EntryMechanicsParser.Classify(
            "Holy Burst",
            MonsterEntrySection.Action,
            "Dexterity Saving Throw: DC 20, each enemy in a 20-foot-radius Sphere centered on " +
            "a point the planetar can see within 120 feet. Failure: 24 (7d6) Radiant damage. " +
            "Success: Half damage.");

        Assert.NotNull(entry.Save!.Area);
        Assert.Equal(AreaShape.Sphere, entry.Save.Area!.Shape);
        Assert.Equal(20, entry.Save.Area.SizeFeet);
        Assert.True(entry.Save.Area.EnemiesOnly);
        Assert.Equal(120, entry.Save.RangeFeet);
        Assert.Equal(["the planetar can see"], entry.UnmodelledClauses);
    }

    [Fact]
    public void EachEnemyInAnEmanationStructuresAnEnemiesOnlyAreaToo()
    {
        // Rakshasa's Baleful Command, verbatim. The same "each enemy" selector on a
        // self-originating Emanation rather than a point-aimed Sphere — pinning that
        // the `selector` group fires across every area branch, not just Sphere's.
        var entry = EntryMechanicsParser.Classify(
            "Baleful Command",
            MonsterEntrySection.Action,
            "Wisdom Saving Throw: DC 18, each enemy in a 30-foot Emanation originating from " +
            "the rakshasa. Failure: 28 (8d6) Psychic damage, and the target has the Frightened " +
            "and Incapacitated conditions until the start of the rakshasa's next turn.");

        Assert.NotNull(entry.Save!.Area);
        Assert.Equal(AreaShape.Emanation, entry.Save.Area!.Shape);
        Assert.Equal(30, entry.Save.Area.SizeFeet);
        Assert.True(entry.Save.Area.EnemiesOnly);
    }

    [Fact]
    public void EachCreatureInASphereIsNotEnemiesOnly()
    {
        // The far more common wording — Adult Green Dragon's Noxious Miasma, verbatim
        // (p.294) — must not regress to EnemiesOnly now that the selector word is read
        // at all: "creature" is the default, unnarrowed reading.
        var entry = EntryMechanicsParser.Classify(
            "Noxious Miasma",
            MonsterEntrySection.LegendaryAction,
            "Constitution Saving Throw: DC 17, each creature in a 20-foot-radius Sphere " +
            "centered on a point the dragon can see within 90 feet. Failure: 7 (2d6) Poison " +
            "damage, and the target takes a -2 penalty to AC until the end of its next turn.");

        Assert.NotNull(entry.Save!.Area);
        Assert.False(entry.Save.Area!.EnemiesOnly);
    }

    #endregion

    #region Target-clause / area agreement (#600)

    [Fact]
    public void EveryCorpusSaveEntryClaimingAnAreaShapeAgreesWithAreaPattern()
    {
        // #600, tightened after adversarial review of this PR's first draft: that
        // version's "all four shapes agree" fixture used a hand-typed Emanation
        // string opening "any creature that starts its turn in a" — which does
        // not match SaveTargetClausePattern's Emanation branch at all (it requires
        // "each creature in a"), so the `areaShape` group never fired for that
        // case and the test's Assert.NotNull(entry.Save?.Area) passed anyway
        // (ParseArea searches the whole text unbounded, so it still found the
        // Emanation regardless of whether the target-clause branch matched it).
        // Removing or misnaming the `areaShape` named group entirely would have
        // left every one of that draft's assertions green.
        //
        // This drives the same claim off the real corpus instead: for every
        // SavingThrow entry whose text SaveTargetClausePattern matches with the
        // `areaShape` group firing, the captured shape word must equal the Shape
        // Classify's own pipeline (ParseArea, via Save.Area) structured for that
        // same text — proving the capture actually reaches real printed syntax,
        // not just a fixture that happens to satisfy a downstream check by a
        // different path. The corpus corroborates each shape directly (verified
        // 2026-09): the aboleth/balor/cloaker/dretch/kraken/solar/vrock family for
        // Emanation, the basilisk/ghoul family for Cone, the black-dragon-acid
        // family for Line, the six chromatic/metallic dragons' breath weapons for
        // the "-radius" Sphere branch #420 added.
        var saveEntries = Monsters
            .SelectMany(monster => monster.Entries)
            .Where(entry => entry.Mechanics == EntryMechanics.SavingThrow)
            .ToList();

        // Guards against a vacuous pass (design's own standing convention, see
        // EveryAttackMechanicsEntryIsActionOrBonusActionSectioned above): if a
        // future extraction change stopped grading anything as SavingThrow, the
        // loop below would silently check nothing.
        Assert.True(saveEntries.Count > 0, "no SavingThrow entries found in the corpus — this test would pass vacuously (#600).");

        var shapesSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in saveEntries)
        {
            var target = EntryMechanicsParser.SaveTargetClausePattern().Match(entry.Text);
            if (!target.Success || !target.Groups["areaShape"].Success)
            {
                continue;
            }

            var claimedShape = target.Groups["areaShape"].Value;
            shapesSeen.Add(claimedShape);

            var classified = EntryMechanicsParser.Classify(entry.Name, entry.Section, entry.Text);

            Assert.True(
                classified.Save?.Area is not null,
                $"'{entry.Name}' — SaveTargetClausePattern's areaShape group captured " +
                $"'{claimedShape}' but Classify's own Save.Area is null (#600 misattribution): " +
                $"\"{entry.Text}\"");

            Assert.Equal(claimedShape, classified.Save!.Area!.Shape.ToString(), StringComparer.Ordinal);
        }

        // Guards against a vacuous pass on a narrower axis than the count check
        // above: the count check only proves SavingThrow entries exist, not that
        // any of them actually exercise the areaShape capture. Pinning the exact
        // shape set means a regression that stopped the group firing for one
        // shape (a typo in the alternation, a corpus entry losing its wording)
        // shows up here rather than the loop above silently checking three
        // shapes instead of four.
        Assert.Equal(
            new[] { "Cone", "Emanation", "Line", "Sphere" },
            shapesSeen.OrderBy(shape => shape, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AnAreaShapeClaimedByTheTargetClauseButNotStructuredByAreaPatternThrows()
    {
        // #600's synthetic pin. The two regexes cannot actually disagree for any
        // text reachable through Classify (see AssertAreaAgreesWithTargetClause's
        // own remarks: whatever SaveTargetClausePattern's area alternatives
        // require as a literal substring is, by construction, always a match for
        // AreaPattern's more permissive superset pattern too) — so this calls the
        // extracted guard directly with the one combination that must never
        // reach here honestly: a claimed shape name with no structured Area.
        // Knockout-verify: stubbing this method's body to a no-op turns this test
        // red, proving it actually exercises the throw rather than passing
        // vacuously.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EntryMechanicsParser.AssertAreaAgreesWithTargetClause(
                "Sphere",
                area: null,
                "each creature in a 20-foot-radius Sphere centered on a point"));

        Assert.Contains("Sphere", exception.Message, StringComparison.Ordinal);
        Assert.Contains("#600", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleTargetClauseWithNoAreaNeverTripsTheAgreementGuard()
    {
        // The single-target branch names no shape at all — `claimedAreaShape` is
        // null exactly when the target clause matched "one creature" rather than
        // an area alternative — so a single target with no structured Area (the
        // overwhelmingly common case; most saves have neither) is not a
        // disagreement and must not throw.
        var exception = Record.Exception(() =>
            EntryMechanicsParser.AssertAreaAgreesWithTargetClause(
                claimedAreaShape: null,
                area: null,
                "one creature"));

        Assert.Null(exception);
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
