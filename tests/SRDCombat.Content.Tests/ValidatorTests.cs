using SRDCombat.Content.Validation;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Covers the checks that exist specifically to catch a bad extraction. Each one is
/// asserted by deliberately breaking a known-good monster in one place, so a passing
/// test means the check actually fires rather than that the fixture happened to be
/// clean.
/// </summary>
public class ValidatorTests
{
    [Fact]
    public void HitPointsDisagreeingWithHitDice_IsAnError()
    {
        // The single strongest check available: the SRD prints "HP 11 (2d8 + 2)" and 11
        // is exactly that expression's average, so a mismatch means one was misread.
        var monster = Monster() with { HitPoints = 12 };

        AssertHasCode(monster, "monster.hit_points.disagree_with_dice", ValidationSeverity.Error);
    }

    [Fact]
    public void ADamageAverageDisagreeingWithItsDice_IsAnError()
    {
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Scimitar",
                    MonsterEntrySection.Action,
                    "Melee Attack Roll: +3, reach 5 ft. Hit: 9 (1d6 + 1) Slashing damage.",
                    new MonsterAttack(
                        AttackKind.Melee,
                        3,
                        5,
                        null,
                        null,
                        [new AttackDamage(DiceExpression.Parse("1d6 + 1"), DamageType.Slashing, 9)])),
            ],
        };

        AssertHasCode(monster, "monster.attack.damage_disagrees_with_average", ValidationSeverity.Error);
    }

    [Fact]
    public void AnAlternativeReplacingAComponentIndexOutsideItsDamageList_IsAnError()
    {
        // #409: AttackRules.RollDamage indexes attack.Damage with ReplacesComponentIndex
        // directly, so an index the extractor could never legitimately produce — here 1,
        // on a single-component attack — would throw at damage resolution. A malformed
        // but loadable content entry must be rejected at validation instead.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Bite",
                    MonsterEntrySection.Action,
                    "Melee Attack Roll: +6, reach 5 ft. Hit: 8 (1d8 + 4) Piercing damage.",
                    new MonsterAttack(
                        AttackKind.Melee,
                        6,
                        5,
                        null,
                        null,
                        [new AttackDamage(DiceExpression.Parse("1d8 + 4"), DamageType.Piercing, 8)])
                    {
                        Alternative = new AlternativeAttackDamage(
                            DiceExpression.Parse("1d4 + 4"),
                            DamageType.Piercing,
                            6,
                            AttackDamageCondition.AttackerIsBloodied)
                        {
                            ReplacesComponentIndex = 1,
                        },
                    }),
            ],
        };

        AssertHasCode(monster, "monster.attack.alternative_index_out_of_range", ValidationSeverity.Error);
    }

    [Fact]
    public void AnAlternativeReplacingAComponentIndexInsideItsDamageList_ProducesNothing()
    {
        // The healthy #409 shape: the em-dash Swarm of Venomous Snakes' Bites, its
        // alternative replacing the Piercing at index 0 of a two-component list (Piercing
        // plus the unconditional Poison). In range, so the trip-wire stays silent.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Bites",
                    MonsterEntrySection.Action,
                    "Melee Attack Roll: +6, reach 5 ft. Hit: 8 (1d8 + 4) Piercing damage-or 6 (1d4 + 4) " +
                    "Piercing damage if the swarm is Bloodied-plus 10 (3d6) Poison damage.",
                    new MonsterAttack(
                        AttackKind.Melee,
                        6,
                        5,
                        null,
                        null,
                        [
                            new AttackDamage(DiceExpression.Parse("1d8 + 4"), DamageType.Piercing, 8),
                            new AttackDamage(DiceExpression.Parse("3d6"), DamageType.Poison, 10),
                        ])
                    {
                        Alternative = new AlternativeAttackDamage(
                            DiceExpression.Parse("1d4 + 4"),
                            DamageType.Piercing,
                            6,
                            AttackDamageCondition.AttackerIsBloodied)
                        {
                            ReplacesComponentIndex = 0,
                        },
                    }),
            ],
        };

        Assert.Empty(MonsterValidator.Validate([monster]).Issues);
    }

    [Fact]
    public void ABundledUseFoldedIntoAMultiattackCompositionWithNoResidue_IsAnError()
    {
        // Mirrors the corpus shape #341/#358 fixed (the Mummy's "makes two Rotting Fist
        // attacks and uses Dreadful Glare") with UnmodelledClauses forced empty, as if a
        // future change silently absorbed the bundled "uses" clause into the composition
        // claim instead of leaving it as residue. See #360.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The bandit makes two Scimitar attacks and uses Dreadful Glare.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Scimitar"], false)),
            ],
        };

        AssertHasCode(monster, "monster.multiattack.bundled_use_dropped", ValidationSeverity.Error);
    }

    [Fact]
    public void ABundledUseFoldedIntoAMultiattackCompositionWithResidue_ProducesNothing()
    {
        // The healthy case: the bundled "uses" clause survived as residue, exactly as
        // #341/#358 fixed it, so the trip-wire stays silent.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The bandit makes two Scimitar attacks and uses Dreadful Glare.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Scimitar"], false),
                    UnmodelledClauses: ["and uses Dreadful Glare"]),
            ],
        };

        Assert.Empty(MonsterValidator.Validate([monster]).Issues);
    }

    [Fact]
    public void AnAlternativeCompositionFoldedIntoAMultiattackCompositionWithNoResidue_IsAnError()
    {
        // #359 (qc's review of #356): every other MonsterValidator check has its own
        // synthetic "..._IsAnError" pin — this trip-wire, added for the Barbed Devil,
        // Clay Golem and Medusa (#342), was exercised only via corpus load until now.
        // Mirrors the corpus shape ("the golem makes two Slam attacks, or it makes
        // three Slam attacks...") with UnmodelledClauses forced empty, as if a future
        // change silently summed the second branch into AttackCount instead of leaving
        // it as residue.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The golem makes two Slam attacks, or it makes three Slam attacks if it used " +
                    "Hasten this turn.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Slam"], false)),
            ],
        };

        AssertHasCode(monster, "monster.multiattack.alternative_composition_dropped", ValidationSeverity.Error);
    }

    [Fact]
    public void AnAlternativeCompositionFoldedIntoAMultiattackCompositionWithResidue_ProducesNothing()
    {
        // The healthy case: the alternative branch survived as residue, exactly as the
        // parser records it today, so the trip-wire stays silent.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The golem makes two Slam attacks, or it makes three Slam attacks if it used " +
                    "Hasten this turn.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Slam"], false),
                    UnmodelledClauses: ["or it makes three Slam attacks if it used Hasten this turn"]),
            ],
        };

        Assert.Empty(MonsterValidator.Validate([monster]).Issues);
    }

    [Fact]
    public void ANamedSubjectAlternativeCompositionThatParserFragmentsUnclaimed_IsAnError()
    {
        // #359's second finding, tightened after qc's Medium on this PR's first
        // attempt: AlternativeCompositionMarker required a pronoun subject ("or
        // it/he/she/they makes"), so a named-subject alternative ("or the golem makes",
        // repeating the same generic noun the composition sentence opens with) would
        // have evaded this trip-wire and EntryMechanicsParser's
        // AlternativeCompositionPattern together — nothing would have caught it.
        // EntryMechanicsParser stays pronoun-only on purpose (a false match there would
        // truncate real extraction), so this fixture is the parser's *actual, verified*
        // output for this shape — not an idealised empty-residue stand-in — pinning that
        // the validator catches the real failure mode: the second clause gets summed
        // into AttackCount (5, not 2) and its residue is fragmented into disconnected
        // scraps ("or the golem", "if it used Hasten this turn") rather than surviving
        // as the single intact clause a recognised alternative leaves. See
        // EntryMechanicsParser's
        // ANamedSubjectAlternativeCompositionIsAStatedParserLimitCaughtByTheValidatorInstead
        // for the parser side of this same pin. Corpus-clean today (confirmed against
        // all 171 Multiattack entries containing "makes").
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The golem makes two Slam attacks, or the golem makes three Slam attacks if it " +
                    "used Hasten this turn.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(5, ["Slam"], false),
                    UnmodelledClauses: ["or the golem", "if it used Hasten this turn"]),
            ],
        };

        AssertHasCode(monster, "monster.multiattack.alternative_composition_dropped", ValidationSeverity.Error);
    }

    [Fact]
    public void AMultiwordNamedSubjectAlternativeCompositionWithNoResidue_IsAnError()
    {
        // The marker's subject accepts up to four words (and hyphens) after "the", not
        // just a bare \w+ — a multiword or hyphenated repeated name ("the clay golem",
        // "the fire-giant") is exactly the shape a single-word check would miss, per
        // qc's Medium finding on this PR's first attempt.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The golem makes two Slam attacks, or the clay golem makes three Slam attacks " +
                    "if it used Hasten this turn.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Slam"], false)),
            ],
        };

        AssertHasCode(monster, "monster.multiattack.alternative_composition_dropped", ValidationSeverity.Error);
    }

    [Fact]
    public void AHyphenatedNamedSubjectAlternativeCompositionWithNoResidue_IsAnError()
    {
        // Pins the marker's "-" specifically (its `[\w-]`, not a bare `\w`): a hyphenated
        // repeated name ("the fire-giant") is a distinct case from the space-separated
        // "the clay golem" above — reducing the subject class to `\w` would keep that test
        // and today's corpus green while silently dropping this one, reopening an
        // undetected named-subject summing case (qc's Medium on this PR's rework).
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The giant makes two Slam attacks, or the fire-giant makes three Slam attacks " +
                    "if it used Hasten this turn.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Slam"], false)),
            ],
        };

        AssertHasCode(monster, "monster.multiattack.alternative_composition_dropped", ValidationSeverity.Error);
    }

    [Fact]
    public void AMakesClauseThatIsNotAnAttackComposition_IsNotFlaggedEvenWithNoResidue()
    {
        // Boundary case from qc's Medium: "or the target makes ..." must not be flagged
        // merely for using "makes" — the marker requires the clause to actually reach
        // an "attack(s)" word within the same sentence, so a saving throw (or any other
        // non-attack use of "makes") does not match at all, regardless of residue.
        // UnmodelledClauses is forced empty (as if some future change absorbed the
        // clause entirely) specifically so this test cannot pass by accident: without
        // the "attacks" requirement, the shorter "or the target makes" would match, and
        // with no residue to contain it, the entry would be wrongly flagged as a
        // dropped attack alternative when it is not an attack composition at all.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The golem makes two Slam attacks, or the target makes a saving throw against " +
                    "poison.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Slam"], false)),
            ],
        };

        Assert.Empty(MonsterValidator.Validate([monster]).Issues);
    }

    [Fact]
    public void ANamedSubjectClauseThatIsNotAMakesClauseAtAll_IsNotFlagged()
    {
        // A second named-subject clause that never uses "makes" at all — "or the golem
        // is Frightened" — is outside this marker's shape entirely, regardless of
        // residue.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Multiattack",
                    MonsterEntrySection.Action,
                    "The golem makes two Slam attacks, or the golem is Frightened.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Slam"], false)),
            ],
        };

        Assert.Empty(MonsterValidator.Validate([monster]).Issues);
    }

    [Fact]
    public void AProficiencyBonusDisagreeingWithChallengeRating_IsAnError()
    {
        var monster = Monster() with { ProficiencyBonus = 5 };

        AssertHasCode(monster, "monster.proficiency.disagrees_with_challenge_rating", ValidationSeverity.Error);
    }

    [Fact]
    public void AnExperienceValueDisagreeingWithChallengeRating_IsOnlyAWarning()
    {
        // Deliberately not an error: the SRD itself is inconsistent here — the Archmage
        // prints CR 12 with XP 8,000 against its own table's 8,400 — and the printed
        // value is kept rather than silently overridden.
        var monster = Monster() with { ExperiencePoints = 30 };

        AssertHasCode(monster, "monster.experience.disagrees_with_challenge_rating", ValidationSeverity.Warning);
        Assert.Empty(MonsterValidator.Validate([monster]).Errors);
    }

    [Fact]
    public void ASaveThatIsNeitherTheModifierNorProficient_IsAWarning()
    {
        // How a lost minus sign in the PDF's text layer surfaces.
        var abilities = Monster().Abilities.ToDictionary(pair => pair.Key, pair => pair.Value);
        abilities[Ability.Intelligence] = new MonsterAbility(Score: 6, SaveBonus: 2);

        AssertHasCode(
            Monster() with { Abilities = abilities },
            "monster.ability.save_unexplained",
            ValidationSeverity.Warning);
    }

    [Fact]
    public void DuplicateIds_AreAnError()
    {
        var result = MonsterValidator.Validate([Monster(), Monster()]);

        Assert.Contains(result.Errors, issue => issue.Code == "monster.id.duplicate");
    }

    [Fact]
    public void AKnownGoodMonster_ProducesNothing() =>
        Assert.Empty(MonsterValidator.Validate([Monster()]).Issues);

    [Fact]
    public void SpellcastingWithoutAUsageTier_IsAnError()
    {
        var monster = Monster() with
        {
            Entries = [new MonsterEntry("Spellcasting", MonsterEntrySection.Action, "The priest casts spells.")],
        };

        var issues = MonsterValidator.Validate([monster], [Spell("Light")]).Issues;

        Assert.Contains(issues, issue => issue.Code == "monster.spellcasting.usage_tier_missing");
    }

    [Fact]
    public void SpellcastingTierNamingAnUnknownSpell_IsAnError()
    {
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Spellcasting",
                    MonsterEntrySection.Action,
                    "The priest casts one of the following spells: At Will: Light, Longstrider."),
            ],
        };

        var issues = MonsterValidator.Validate([monster], [Spell("Light")]).Issues;

        Assert.Contains(issues, issue => issue.Code == "monster.spellcasting.spell_unknown"
            && issue.Message.Contains("Longstrider", StringComparison.Ordinal));
    }

    [Fact]
    public void SpellcastingSpellNotesWithCommas_AreNotMistakenForSpellNames()
    {
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Spellcasting",
                    MonsterEntrySection.Action,
                    "The dragon casts one of the following spells: At Will: Shapechange " +
                    "(Beast or Humanoid form only, no Temporary Hit Points gained), Speak with Animals."),
            ],
        };

        Assert.Empty(MonsterValidator.Validate([monster], [Spell("Shapechange"), Spell("Speak with Animals")]).Issues);
    }

    [Fact]
    public void LegendaryActionEntriesWithoutAUsesCount_IsAnError()
    {
        // The Uses count is a separate extracted field (#423) — an entry present with
        // no count means the preamble was lost rather than merely misplaced.
        var monster = Monster() with
        {
            Entries = [new MonsterEntry("Lash", MonsterEntrySection.LegendaryAction, "The dragon makes one attack.")],
        };

        AssertHasCode(monster, "monster.legendary_action_uses.missing", ValidationSeverity.Error);
    }

    [Fact]
    public void ALegendaryActionUsesCountWithoutAnyLegendaryActionEntries_IsAnError()
    {
        var monster = Monster() with { LegendaryActionUses = 3 };

        AssertHasCode(monster, "monster.legendary_action_uses.unexpected", ValidationSeverity.Error);
    }

    [Fact]
    public void AnEntryStillCarryingTheLegendaryActionsPreamble_IsAnError()
    {
        // The regression guard #423 asks for directly: nothing extracted after this fix
        // should ever fold "Legendary Action Uses" prose into an entry's own text.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Dominate Mind",
                    MonsterEntrySection.Action,
                    "Wisdom Saving Throw: DC 16. Legendary Action Uses: 3 (4 in Lair). Immediately after " +
                    "another creature's turn, the aboleth can expend a use to take one of the following " +
                    "actions. The aboleth regains all expended uses at the start of each of its turns."),
                new MonsterEntry("Lash", MonsterEntrySection.LegendaryAction, "The aboleth makes one attack."),
            ],
            LegendaryActionUses = 3,
            LegendaryActionUsesInLair = 4,
        };

        AssertHasCode(monster, "monster.entry.legendary_preamble_embedded", ValidationSeverity.Error);
    }

    [Fact]
    public void AWellFormedLegendaryActionsSection_ProducesNothing()
    {
        var monster = Monster() with
        {
            Entries = [new MonsterEntry("Lash", MonsterEntrySection.LegendaryAction, "The dragon makes one attack.")],
            LegendaryActionUses = 3,
            LegendaryActionUsesInLair = 4,
        };

        Assert.Empty(MonsterValidator.Validate([monster]).Issues);
    }

    [Fact]
    public void AVersatileWeaponWithoutTwoHandedDamage_IsAnError()
    {
        var weapon = Weapon() with { Properties = WeaponProperty.Versatile, VersatileDamage = null };

        var result = EquipmentValidator.ValidateWeapons([weapon]);

        Assert.Contains(result.Errors, issue => issue.Code == "weapon.versatile.inconsistent");
    }

    [Fact]
    public void AnAmmunitionWeaponWithoutARangeBand_IsAnError()
    {
        var weapon = Weapon() with
        {
            Kind = WeaponKind.Ranged,
            Properties = WeaponProperty.Ammunition,
            AmmunitionKind = "Bolt",
            Range = null,
        };

        var result = EquipmentValidator.ValidateWeapons([weapon]);

        Assert.Contains(result.Errors, issue => issue.Code == "weapon.range.inconsistent");
    }

    [Fact]
    public void MediumArmorWithoutADexterityCap_IsAnError()
    {
        var armor = new ArmorDefinition
        {
            Id = "armor.test",
            Name = "Test",
            Category = ArmorCategory.Medium,
            BaseArmorClass = 13,
            AddsDexterityModifier = true,
            MaximumDexterityModifier = null,
            StealthDisadvantage = false,
            WeightPounds = 20m,
            CostCopper = 5_000,
        };

        var result = EquipmentValidator.ValidateArmor([armor]);

        Assert.Contains(result.Errors, issue => issue.Code == "armor.dexterity.missing_cap");
    }

    [Fact]
    public void ThrowIfInvalid_NamesEveryError()
    {
        var result = MonsterValidator.Validate([Monster() with { HitPoints = 12, ProficiencyBonus = 5 }]);

        var exception = Assert.Throws<ContentValidationException>(() => result.ThrowIfInvalid("monsters.json"));

        Assert.Contains("monster.hit_points.disagree_with_dice", exception.Message, StringComparison.Ordinal);
        Assert.Contains("monster.proficiency.disagrees_with_challenge_rating", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertHasCode(MonsterDefinition monster, string code, ValidationSeverity severity)
    {
        var issues = MonsterValidator.Validate([monster]).Issues;

        Assert.Contains(issues, issue => issue.Code == code && issue.Severity == severity);
    }

    /// <summary>A valid monster, modelled on the printed Bandit stat block.</summary>
    private static MonsterDefinition Monster() => new()
    {
        Id = "monster.bandit",
        Name = "Bandit",
        Sizes = [CreatureSize.Medium],
        Type = CreatureType.Humanoid,
        Alignment = "Neutral",
        ArmorClass = 12,
        InitiativeBonus = 1,
        HitPoints = 11,
        HitDice = DiceExpression.Parse("2d8 + 2"),
        Speeds = new Dictionary<MovementMode, int> { [MovementMode.Walk] = 30 },
        Abilities = new Dictionary<Ability, MonsterAbility>
        {
            [Ability.Strength] = new(11, 0),
            [Ability.Dexterity] = new(12, 1),
            [Ability.Constitution] = new(12, 1),
            [Ability.Intelligence] = new(10, 0),
            [Ability.Wisdom] = new(10, 0),
            [Ability.Charisma] = new(10, 0),
        },
        Skills = new Dictionary<string, int>(),
        DamageResponses = new Dictionary<DamageType, DamageResponse>(),
        ConditionImmunities = [],
        Senses = [],
        PassivePerception = 10,
        Languages = ["Common"],
        Gear = [],
        ChallengeRating = 0.125m,
        ExperiencePoints = 25,
        ProficiencyBonus = 2,
        Entries = [],
        SourcePage = 261,
    };

    private static SpellDefinition Spell(string name) => new()
    {
        Id = "spell." + name.ToLowerInvariant(),
        Name = name,
        Level = 0,
        School = MagicSchool.Evocation,
        Classes = [],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        RangeText = "Self",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Text = string.Empty,
        Mechanics = EntryMechanics.Unmodelled,
        SourcePage = 1,
    };

    /// <summary>A valid weapon, modelled on the printed Longsword row.</summary>
    private static WeaponDefinition Weapon() => new()
    {
        Id = "weapon.longsword",
        Name = "Longsword",
        Category = WeaponCategory.Martial,
        Kind = WeaponKind.Melee,
        Damage = DiceExpression.Parse("1d8"),
        DamageType = DamageType.Slashing,
        Properties = WeaponProperty.Versatile,
        VersatileDamage = DiceExpression.Parse("1d10"),
        Mastery = WeaponMastery.Sap,
        WeightPounds = 3m,
        CostCopper = 1_500,
    };
}
