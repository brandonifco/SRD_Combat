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
}
