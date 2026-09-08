using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Pins the on-disk format. These stand in for the hand-mirrored DTO layer this project
/// deliberately does not have — see the remarks on <see cref="ContentSerializer"/> —
/// and they are what makes a change to the serialized shape fail loudly instead of
/// silently rewriting every content file on the next extraction.
/// </summary>
public class ContentSerializerTests
{
    [Fact]
    public void Serialize_WritesDiceAsTheirPrintedForm()
    {
        // Deliberately a real content type rather than an anonymous one: anonymous type
        // properties are genuinely read-only, so IgnoreReadOnlyProperties skips them
        // entirely. Every content record uses init accessors and is unaffected.
        var json = ContentSerializer.Serialize(
            new AttackDamage(DiceExpression.Parse("2d6 + 3"), DamageType.Slashing, 10));

        Assert.Contains("\"2d6 + 3\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_OmitsDerivedValues()
    {
        // Modifier is computed from Score. Persisting it would let a hand-edited file
        // hold a score and a modifier that disagree.
        var json = ContentSerializer.Serialize(new MonsterAbility(Score: 17, SaveBonus: 3));

        Assert.Contains("\"score\": 17", json, StringComparison.Ordinal);
        Assert.DoesNotContain("modifier", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_OmitsAbsentOptionalValues()
    {
        var json = ContentSerializer.Serialize(new MonsterEntry(
            "Nimble Escape",
            MonsterEntrySection.BonusAction,
            "The goblin takes the Disengage or Hide action."));

        Assert.DoesNotContain("attack", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_WritesEnumsAsNamesAndLeavesDictionaryKeysAlone()
    {
        var pack = new ContentPack<Dictionary<MovementMode, int>>
        {
            FormatVersion = ContentSerializer.CurrentFormatVersion,
            Content = "test",
            Source = "test",
            Items = [new Dictionary<MovementMode, int> { [MovementMode.Fly] = 60 }],
        };

        var json = ContentSerializer.Serialize(pack);

        // Property names are camelCased because they are schema; dictionary keys are
        // data and keep the enum's own casing.
        Assert.Contains("\"formatVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Fly\": 60", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RejectsUnknownProperties()
    {
        // A typo in a content file must fail rather than be skipped. This is the check
        // that a DTO mirror would have got wrong by default.
        const string Json = """
            {"formatVersion":1,"content":"test","source":"test","items":[],"oops":true}
            """;

        Assert.ThrowsAny<Exception>(() => ContentSerializer.Deserialize<ContentPack<string>>(Json));
    }

    [Fact]
    public void RoundTrip_PreservesAMonster()
    {
        var original = new MonsterDefinition
        {
            Id = "monster.test-subject",
            Name = "Test Subject",
            Sizes = [CreatureSize.Medium, CreatureSize.Small],
            Type = CreatureType.Fey,
            Subtype = "Goblinoid",
            Alignment = "Chaotic Neutral",
            ArmorClass = 15,
            InitiativeBonus = 2,
            HitPoints = 10,
            HitDice = DiceExpression.Parse("3d6"),
            Speeds = new Dictionary<MovementMode, int> { [MovementMode.Walk] = 30, [MovementMode.Fly] = 60 },
            CanHover = true,
            Abilities = Enum.GetValues<Ability>().ToDictionary(a => a, _ => new MonsterAbility(12, 1)),
            Skills = new Dictionary<string, int> { ["Animal Handling"] = 6 },
            DamageResponses = new Dictionary<DamageType, DamageResponse>
            {
                [DamageType.Fire] = DamageResponse.Immunity,
            },
            ConditionImmunities = [ConditionType.Poisoned],
            Senses = [new MonsterSense(SenseType.Darkvision, 60)],
            PassivePerception = 9,
            Languages = ["Common"],
            Gear = ["Scimitar"],
            ChallengeRating = 0.25m,
            ExperiencePoints = 50,
            ProficiencyBonus = 2,
            Entries =
            [
                new MonsterEntry(
                    "Scimitar",
                    MonsterEntrySection.Action,
                    "Melee Attack Roll: +4, reach 5 ft. Hit: 5 (1d6 + 2) Slashing damage.",
                    new MonsterAttack(
                        AttackKind.Melee,
                        4,
                        ReachFeet: 5,
                        NormalRangeFeet: null,
                        LongRangeFeet: null,
                        [new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Slashing, 5)])),
            ],
            SourcePage = 290,
        };

        var restored = ContentSerializer.Deserialize<MonsterDefinition>(ContentSerializer.Serialize(original));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Sizes, restored.Sizes);
        Assert.Equal(original.Subtype, restored.Subtype);
        Assert.Equal(original.HitDice, restored.HitDice);
        Assert.Equal(original.Speeds, restored.Speeds);
        Assert.True(restored.CanHover);
        Assert.Equal(original.ChallengeRating, restored.ChallengeRating);

        // A multi-word dictionary key must survive intact rather than being recased.
        Assert.Equal(6, restored.Skills["Animal Handling"]);

        var attack = Assert.IsType<MonsterAttack>(Assert.Single(restored.Entries).Attack);
        Assert.Equal("1d6 + 2", Assert.Single(attack.Damage).Amount.ToString());
    }

    [Fact]
    public void RoundTrip_PreservesASpellsEvilCasterDamageType()
    {
        // Spirit Guardians' alignment-alternative damage type (#375). This is the one
        // field UnmappedMemberHandling.Disallow exists to catch a miss on: a property
        // added to SpellDefinition with no serializer round trip pinned would still
        // load fine locally against a freshly regenerated spells.json and only fail on
        // a committed file that predates the field.
        var original = new SpellDefinition
        {
            Id = "spell.spirit-guardians",
            Name = "Spirit Guardians",
            Level = 3,
            School = MagicSchool.Conjuration,
            Classes = ["Cleric"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "Self",
            Components = SpellComponents.Verbal | SpellComponents.Somatic | SpellComponents.Material,
            DurationText = "Concentration, up to 10 minutes",
            RequiresConcentration = true,
            Text = "takes 3d8 Radiant damage (if you are good or neutral) or 3d8 Necrotic damage (if you are evil)",
            Mechanics = EntryMechanics.SavingThrow,
            Save = new SaveEffect(
                Ability.Wisdom,
                DifficultyClass: null,
                new EffectArea(AreaShape.Emanation, 15),
                [new AttackDamage(DiceExpression.Parse("3d8"), DamageType.Radiant, 13)],
                SaveSuccessOutcome.HalfDamage,
                []),
            Damage = [new AttackDamage(DiceExpression.Parse("3d8"), DamageType.Radiant, 13)],
            EvilCasterDamageType = DamageType.Necrotic,
            SourcePage = 164,
        };

        var json = ContentSerializer.Serialize(original);

        Assert.Contains("\"evilCasterDamageType\": \"Necrotic\"", json, StringComparison.Ordinal);

        var restored = ContentSerializer.Deserialize<SpellDefinition>(json);

        Assert.Equal(DamageType.Necrotic, restored.EvilCasterDamageType);
        Assert.Equal(original.Damage, restored.Damage);
        Assert.Equal(original.Save!.FailureDamage, restored.Save!.FailureDamage);
    }

    /// <summary>
    /// The Chimera's Bite (#371): "Hit: 11 (2d6 + 4) Piercing damage, or 18 (4d6 + 4)
    /// Piercing damage if the chimera had Advantage on the attack roll."
    /// <see cref="MonsterAttack.Alternative"/> is the field that structures the "or…if"
    /// tier, and like <c>EvilCasterDamageType</c> above, <c>UnmappedMemberHandling.Disallow</c>
    /// only catches a miss on it once something actually asks the serializer to write and
    /// read every one of its fields — the corpus round-trip test's whole-entry equality
    /// check covers <c>Alternative</c> today only incidentally, never pinned on its own
    /// (#410, filed off #371/#408's review).
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesAMonsterAttacksAlternativeDamage()
    {
        var original = new MonsterAttack(
            AttackKind.Melee,
            AttackBonus: 7,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("2d6 + 4"), DamageType.Piercing, 11)])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("4d6 + 4"),
                DamageType.Piercing,
                18,
                AttackDamageCondition.AttackRollHadAdvantage),
        };

        var json = ContentSerializer.Serialize(original);

        Assert.Contains("\"amount\": \"4d6 + 4\"", json, StringComparison.Ordinal);
        Assert.Contains("\"condition\": \"AttackRollHadAdvantage\"", json, StringComparison.Ordinal);

        var restored = ContentSerializer.Deserialize<MonsterAttack>(json);

        Assert.NotNull(restored.Alternative);
        Assert.Equal(original.Alternative!.Amount, restored.Alternative!.Amount);
        Assert.Equal(original.Alternative.Type, restored.Alternative.Type);
        Assert.Equal(original.Alternative.PrintedAverage, restored.Alternative.PrintedAverage);
        Assert.Equal(original.Alternative.Condition, restored.Alternative.Condition);

        // The whole-list #371 shape leaves ReplacesComponentIndex null, and a null field
        // is not written at all (JsonIgnoreCondition.WhenWritingNull) — which is what
        // keeps #371's eight structured entries byte-for-byte unchanged by #409.
        Assert.Null(original.Alternative.ReplacesComponentIndex);
        Assert.DoesNotContain("replacesComponentIndex", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(restored.Alternative.ReplacesComponentIndex);

        // The base component is untouched by the alternative round-tripping alongside it.
        Assert.Equal(original.Damage, restored.Damage);
    }

    /// <summary>
    /// The Swarm of Venomous Snakes' Bites (#409): the em-dash "or…if…plus" chain whose
    /// alternative replaces one named component (<see cref="AlternativeAttackDamage.ReplacesComponentIndex"/>)
    /// rather than the whole <see cref="MonsterAttack.Damage"/> list, and whose
    /// unconditional Poison "plus" is a second base component. Pins that both the index
    /// and the multi-component Damage list survive the round trip — the shape
    /// <c>UnmappedMemberHandling.Disallow</c> only guards once something writes and reads
    /// every field.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesAPerComponentAlternativeAndItsUnconditionalPlus()
    {
        var original = new MonsterAttack(
            AttackKind.Melee,
            AttackBonus: 6,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
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
        };

        var json = ContentSerializer.Serialize(original);

        Assert.Contains("\"replacesComponentIndex\": 0", json, StringComparison.Ordinal);

        var restored = ContentSerializer.Deserialize<MonsterAttack>(json);

        Assert.NotNull(restored.Alternative);
        Assert.Equal(0, restored.Alternative!.ReplacesComponentIndex);
        Assert.Equal(AttackDamageCondition.AttackerIsBloodied, restored.Alternative.Condition);

        // Both base components — the Piercing the alternative replaces and the always-on
        // Poison "plus" — survive in printed order.
        Assert.Equal(original.Damage, restored.Damage);
    }

    /// <summary>
    /// The eight header-parenthetical entries (#666): <see cref="MonsterAttack.AdvantageCondition"/>
    /// is the field that structures "(with Advantage if the target …)", carried onto
    /// <see cref="SRDCombat.Core.Combat.CombatAttack"/> beside <see cref="MonsterAttack.Alternative"/>.
    /// Like <c>Alternative</c> before #410, nothing else round-trips it on its own.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesAMonsterAttacksAdvantageCondition()
    {
        var original = new MonsterAttack(
            AttackKind.Melee,
            AttackBonus: 5,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("2d6 + 3"), DamageType.Slashing, 10)])
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsGrappledByAttacker,
        };

        var json = ContentSerializer.Serialize(original);

        Assert.Contains("\"advantageCondition\": \"TargetIsGrappledByAttacker\"", json, StringComparison.Ordinal);

        var restored = ContentSerializer.Deserialize<MonsterAttack>(json);

        Assert.Equal(AttackRollAdvantageCondition.TargetIsGrappledByAttacker, restored.AdvantageCondition);

        // Null — the overwhelming majority of attacks — is not written at all
        // (JsonIgnoreCondition.WhenWritingNull), keeping every other attack's JSON
        // byte-for-byte unchanged.
        var withoutCondition = original with { AdvantageCondition = null };
        var jsonWithout = ContentSerializer.Serialize(withoutCondition);

        Assert.DoesNotContain("advantageCondition", jsonWithout, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ContentSerializer.Deserialize<MonsterAttack>(jsonWithout).AdvantageCondition);
    }

    /// <summary>
    /// #386's engine half: <see cref="SaveEffect.RangeFeet"/> is the field an entry
    /// save's printed range will reach through once the extraction half structures it.
    /// Pins that a populated value round-trips — the same contract
    /// <see cref="RoundTrip_PreservesASpellsEvilCasterDamageType"/> pins for
    /// <c>EvilCasterDamageType</c>, and for the identical reason:
    /// <c>UnmappedMemberHandling.Disallow</c> exists to catch exactly the miss where a
    /// field is added to the runtime type but no test ever asks the serializer to
    /// write or read it.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesASaveEffectsPrintedRange()
    {
        var original = new SaveEffect(
            Ability.Wisdom,
            DifficultyClass: 11,
            Area: null,
            FailureDamage: [],
            SaveSuccessOutcome.NoEffect,
            AppliedConditions: [new AppliedCondition(ConditionType.Frightened)],
            RangeFeet: 30);

        var json = ContentSerializer.Serialize(original);

        Assert.Contains("\"rangeFeet\": 30", json, StringComparison.Ordinal);

        var restored = ContentSerializer.Deserialize<SaveEffect>(json);

        Assert.Equal(30, restored.RangeFeet);
    }

    /// <summary>
    /// The default, and every entry in the corpus today (#386's extraction half has not
    /// landed): no printed range reached the structure, so nothing about a range
    /// appears in the file at all — not a zero, not an explicit null.
    /// </summary>
    [Fact]
    public void Serialize_OmitsAnAbsentSaveEffectRange()
    {
        var json = ContentSerializer.Serialize(new SaveEffect(
            Ability.Wisdom,
            DifficultyClass: 11,
            Area: null,
            FailureDamage: [],
            SaveSuccessOutcome.NoEffect,
            AppliedConditions: []));

        Assert.DoesNotContain("rangeFeet", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_OmitsEvilCasterDamageTypeForEverySpellButSpiritGuardians()
    {
        var json = ContentSerializer.Serialize(new SpellDefinition
        {
            Id = "spell.fire-bolt",
            Name = "Fire Bolt",
            Level = 0,
            School = MagicSchool.Evocation,
            Classes = ["Sorcerer"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "120 feet",
            RangeFeet = 120,
            Components = SpellComponents.Verbal | SpellComponents.Somatic,
            DurationText = "Instantaneous",
            Text = "You hurl a mote of fire.",
            Mechanics = EntryMechanics.Attack,
            IsSpellAttack = true,
            Damage = [new AttackDamage(DiceExpression.Parse("1d10"), DamageType.Fire, 6)],
            SourcePage = 130,
        });

        Assert.DoesNotContain("evilCasterDamageType", json, StringComparison.OrdinalIgnoreCase);
    }
}
