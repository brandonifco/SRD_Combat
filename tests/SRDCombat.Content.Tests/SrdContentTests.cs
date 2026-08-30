using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Loads the real committed SRD content. This is Phase 0's acceptance test: it proves
/// the extractor's output is something the game can actually read, and that the values
/// in it match the book.
/// </summary>
/// <remarks>
/// The spot checks below are deliberately checked against the printed SRD rather than
/// against whatever the extractor happened to produce — a test written from the output
/// would pass just as happily on wrong output.
/// </remarks>
public class SrdContentTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void Load_ReadsTheWholeBestiaryAndEquipmentTables()
    {
        // The Weapons and Armor tables are small and closed, so exact counts are a
        // meaningful regression check: 38 weapons and 13 armor entries including Shield.
        Assert.Equal(38, Content.Weapons.Count);
        Assert.Equal(13, Content.Armor.Count);

        // The bestiary has no published count to assert against, so this guards against
        // a parser regression silently dropping creatures rather than pinning a number
        // the SRD states.
        Assert.True(
            Content.Monsters.Count >= 330,
            $"Expected at least 330 monsters, found {Content.Monsters.Count}.");
    }

    [Fact]
    public void Load_ValidatesEveryFile()
    {
        var result = ContentLoader.Validate(RepositoryPaths.SrdContentDirectory);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Monsters_MatchThePrintedStatBlock()
    {
        var bandit = Content.MonstersById["monster.bandit"];

        Assert.Equal("Bandit", bandit.Name);
        Assert.Equal(CreatureType.Humanoid, bandit.Type);
        Assert.Equal(12, bandit.ArmorClass);
        Assert.Equal(11, bandit.HitPoints);
        Assert.Equal("2d8 + 2", bandit.HitDice.ToString());
        Assert.Equal(30, bandit.Speeds[MovementMode.Walk]);
        Assert.Equal(0.125m, bandit.ChallengeRating);
        Assert.Equal(25, bandit.ExperiencePoints);

        // "Medium or Small Humanoid" — both sizes are kept, in printed order.
        Assert.Equal([CreatureSize.Medium, CreatureSize.Small], bandit.Sizes);
    }

    [Fact]
    public void Monsters_CarryStructuredAttacks()
    {
        var scimitar = Content
            .MonstersById["monster.bandit"]
            .Entries
            .Single(entry => entry.Name == "Scimitar");

        var attack = Assert.IsType<MonsterAttack>(scimitar.Attack);

        // "Melee Attack Roll: +3, reach 5 ft. Hit: 4 (1d6 + 1) Slashing damage."
        Assert.Equal(AttackKind.Melee, attack.Kind);
        Assert.Equal(3, attack.AttackBonus);
        Assert.Equal(5, attack.ReachFeet);
        Assert.Null(attack.NormalRangeFeet);

        var damage = Assert.Single(attack.Damage);
        Assert.Equal("1d6 + 1", damage.Amount.ToString());
        Assert.Equal(DamageType.Slashing, damage.Type);
        Assert.Equal(4, damage.PrintedAverage);
    }

    [Fact]
    public void Monsters_CarryRangedAttacksWithBothRangeBands()
    {
        var shortbow = Content
            .MonstersById["monster.goblin-warrior"]
            .Entries
            .Single(entry => entry.Name == "Shortbow");

        var attack = Assert.IsType<MonsterAttack>(shortbow.Attack);

        Assert.Equal(AttackKind.Ranged, attack.Kind);
        Assert.Equal(80, attack.NormalRangeFeet);
        Assert.Equal(320, attack.LongRangeFeet);
    }

    [Fact]
    public void Monsters_MarkConditionalDamageAsConditional()
    {
        // "Hit: 5 (1d6 + 2) Slashing damage, plus 2 (1d4) Slashing damage if the attack
        // roll had Advantage." The qualifier belongs to the second component only —
        // attaching it to both, or to neither, are the two ways to get this wrong, and
        // both are silent at runtime.
        var scimitar = Content
            .MonstersById["monster.goblin-warrior"]
            .Entries
            .Single(entry => entry.Name == "Scimitar");

        var damage = Assert.IsType<MonsterAttack>(scimitar.Attack).Damage;

        Assert.Equal(2, damage.Count);
        Assert.Null(damage[0].Condition);
        Assert.Equal(AttackDamageCondition.AttackRollHadAdvantage, damage[1].Condition);
    }

    [Fact]
    public void Monsters_DoNotTreatAFollowingSentenceAsADamageCondition()
    {
        // The Mummy reads "... plus 10 (3d6) Necrotic damage. If the target is a
        // creature, it is cursed." That "If" opens a new sentence describing a rider, not
        // a condition on the damage, so the necrotic damage must stay unconditional.
        var fist = Content
            .MonstersById["monster.mummy"]
            .Entries
            .Single(entry => entry.Name == "Rotting Fist");

        Assert.All(
            Assert.IsType<MonsterAttack>(fist.Attack).Damage,
            component => Assert.Null(component.Condition));
    }

    [Fact]
    public void Monsters_UseThe2024Taxonomy()
    {
        // A Goblin is Fey in the 2024 rules, not Humanoid as it was in 5.1. This is the
        // clearest single proof that the content came from SRD 5.2.1.
        var goblin = Content.MonstersById["monster.goblin-warrior"];

        Assert.Equal(CreatureType.Fey, goblin.Type);
        Assert.Equal("Goblinoid", goblin.Subtype);
    }

    [Fact]
    public void Monsters_SeparateTraitsFromActionsAndBonusActions()
    {
        var goblin = Content.MonstersById["monster.goblin-warrior"];

        var nimbleEscape = goblin.Entries.Single(entry => entry.Name == "Nimble Escape");

        Assert.Equal(MonsterEntrySection.BonusAction, nimbleEscape.Section);
        Assert.Null(nimbleEscape.Attack);
    }

    [Fact]
    public void Monsters_RecordConditionImmunitiesSeparatelyFromDamageImmunities()
    {
        // "Immunities Fire, Poison; Poisoned" — the semicolon divides damage types from
        // conditions, and the two must not end up in the same bucket.
        var azer = Content.MonstersById["monster.azer-sentinel"];

        Assert.Equal(DamageResponse.Immunity, azer.DamageResponses[DamageType.Fire]);
        Assert.Equal(DamageResponse.Immunity, azer.DamageResponses[DamageType.Poison]);
        Assert.Contains(ConditionType.Poisoned, azer.ConditionImmunities);
    }

    [Fact]
    public void Monsters_ReadLairExperienceWhereThePrintedBlockHasIt()
    {
        // "CR 14 (XP 11,500, or 13,000 in lair; PB +5)"
        var dragon = Content.MonstersById["monster.adult-black-dragon"];

        Assert.Equal(14m, dragon.ChallengeRating);
        Assert.Equal(11_500, dragon.ExperiencePoints);
        Assert.Equal(13_000, dragon.LairExperiencePoints);
    }

    [Fact]
    public void Weapons_MatchThePrintedTable()
    {
        var greataxe = Content.WeaponsById["weapon.greataxe"];

        // "Greataxe  1d12 Slashing  Heavy, Two-Handed  Cleave  7 lb.  30 GP"
        Assert.Equal(WeaponCategory.Martial, greataxe.Category);
        Assert.Equal(WeaponKind.Melee, greataxe.Kind);
        Assert.Equal("1d12", greataxe.Damage.ToString());
        Assert.Equal(DamageType.Slashing, greataxe.DamageType);
        Assert.Equal(WeaponProperty.Heavy | WeaponProperty.TwoHanded, greataxe.Properties);
        Assert.Equal(WeaponMastery.Cleave, greataxe.Mastery);
        Assert.Equal(7m, greataxe.WeightPounds);
        Assert.Equal(3_000, greataxe.CostCopper);
    }

    [Fact]
    public void Weapons_UnpackVersatileAndAmmunitionProperties()
    {
        var longsword = Content.WeaponsById["weapon.longsword"];
        Assert.True(longsword.Properties.HasFlag(WeaponProperty.Versatile));
        Assert.Equal("1d10", longsword.VersatileDamage?.ToString());

        // "Heavy Crossbow  1d10 Piercing  Ammunition (Range 100/400; Bolt), Heavy, Loading, Two-Handed"
        // This row is also where naive text extraction collides the Name and Damage
        // columns, so it doubles as a check on the table parser.
        var heavyCrossbow = Content.WeaponsById["weapon.heavy-crossbow"];
        Assert.Equal("Heavy Crossbow", heavyCrossbow.Name);
        Assert.Equal("1d10", heavyCrossbow.Damage.ToString());
        Assert.Equal("Bolt", heavyCrossbow.AmmunitionKind);
        Assert.Equal(100, heavyCrossbow.Range?.NormalFeet);
        Assert.Equal(400, heavyCrossbow.Range?.LongFeet);
        Assert.True(heavyCrossbow.Properties.HasFlag(WeaponProperty.Loading));
        Assert.True(heavyCrossbow.Properties.HasFlag(WeaponProperty.TwoHanded));
    }

    [Fact]
    public void Weapons_AllCarryAMasteryProperty()
    {
        // Every weapon in the 2024 table has one, and it is most of what gives a martial
        // character a decision to make on their turn.
        Assert.All(Content.Weapons, weapon => Assert.True(Enum.IsDefined(weapon.Mastery)));
    }

    [Fact]
    public void Armor_MatchesThePrintedTable()
    {
        var plate = Content.ArmorById["armor.plate-armor"];

        Assert.Equal(ArmorCategory.Heavy, plate.Category);
        Assert.Equal(18, plate.BaseArmorClass);
        Assert.False(plate.AddsDexterityModifier);
        Assert.Equal(15, plate.MinimumStrength);
        Assert.True(plate.StealthDisadvantage);
        Assert.Equal(150_000, plate.CostCopper);

        var chainShirt = Content.ArmorById["armor.chain-shirt"];
        Assert.Equal(ArmorCategory.Medium, chainShirt.Category);
        Assert.Equal(13, chainShirt.BaseArmorClass);
        Assert.True(chainShirt.AddsDexterityModifier);
        Assert.Equal(2, chainShirt.MaximumDexterityModifier);

        var shield = Content.ArmorById["armor.shield"];
        Assert.Equal(ArmorCategory.Shield, shield.Category);
        Assert.Equal(2, shield.BaseArmorClass);
    }

    [Fact]
    public void Monsters_HaveEnoughLowChallengeRatingsForATierOneGauntlet()
    {
        // The game runs levels 1-5, and its encounter budgets are spent on creatures in
        // roughly the CR 0-4 band. If that band were thin, the ladder could not be built
        // from SRD content at all — so this is a content-scope check, not a parser one.
        var tierOne = Content.Monsters.Count(monster => monster.ChallengeRating <= 4m);

        Assert.True(tierOne >= 150, $"Only {tierOne} monsters are CR 4 or below.");
    }
}
