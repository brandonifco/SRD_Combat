using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Core.Tests.Combat;

namespace SRDCombat.Core.Tests.Characters;

public class CastingTests
{
    [Theory]
    [InlineData(2, 3, 13)]
    [InlineData(3, 4, 15)]
    public void SpellSaveDifficultyClassIsEightPlusProficiencyPlusAbility(
        int proficiency,
        int ability,
        int expected) =>
        Assert.Equal(expected, SpellcastingRules.SaveDifficultyClass(proficiency, ability));

    [Theory]
    // DC 10, or half the damage if that is higher.
    [InlineData(6, 10)]
    [InlineData(20, 10)]
    [InlineData(22, 11)]
    [InlineData(40, 20)]
    public void ConcentrationDifficultyClassIsTenOrHalfTheDamage(int damage, int expected) =>
        Assert.Equal(expected, SpellcastingRules.ConcentrationDifficultyClass(damage));

    [Fact]
    public void EveryCastingClassHasASpellcastingAbility()
    {
        // The Paladin is why this is a curated map rather than read from Primary Ability:
        // its primary abilities are Strength and Charisma, and it casts on Charisma.
        Assert.Equal(Ability.Charisma, SpellcastingRules.AbilityFor("class.paladin"));
        Assert.Equal(Ability.Wisdom, SpellcastingRules.AbilityFor("class.cleric"));
        Assert.Equal(Ability.Intelligence, SpellcastingRules.AbilityFor("class.wizard"));

        Assert.Null(SpellcastingRules.AbilityFor("class.fighter"));
    }

    [Fact]
    public void AnAttackSpellRollsAgainstArmorClassAndDealsDamage()
    {
        var (encounter, caster, target) = Fight([FireBolt()]);

        Assert.Null(encounter.CastSpell("spell.fire-bolt", target));

        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.SpellCast);
        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.Damage);
        Assert.True(target.CurrentHitPoints < target.Stats.MaximumHitPoints);

        // A cantrip costs no slot.
        Assert.Equal(2, caster.Features.SpellSlotsRemaining[1]);
    }

    [Fact]
    public void ALevelledSpellSpendsASlot()
    {
        var (encounter, caster, target) = Fight([MagicBolt()]);

        Assert.Equal(2, caster.Features.SpellSlotsRemaining[1]);
        Assert.Null(encounter.CastSpell("spell.magic-bolt", target));
        Assert.Equal(1, caster.Features.SpellSlotsRemaining[1]);
    }

    [Fact]
    public void CastingIsRefusedWithNoSlotsLeft()
    {
        var (encounter, caster, target) = Fight([MagicBolt()]);

        // Every slot of the spell's level *or higher* has to be gone: a level 2 slot
        // legitimately casts a level 1 spell.
        caster.Features.SpellSlotsRemaining[1] = 0;
        caster.Features.SpellSlotsRemaining[2] = 0;

        Assert.Equal("spell.no_slot", encounter.CastSpell("spell.magic-bolt", target)?.Code);
    }

    [Fact]
    public void ASaveSpellCatchesEveryCreatureInItsArea()
    {
        var caster = Caster("caster", [Fireball()], initiative: 10);

        // Three goblins clustered inside a 20-foot Sphere, one well outside it.
        var near = Enumerable.Range(0, 3)
            .Select(index => CombatTestData.Combatant(
                $"goblin-{index}",
                sideId: CombatTestData.Monsters,
                x: 6 + index,
                y: 5,
                stats: CombatTestData.Stats(maximumHitPoints: 40, armorClass: 10)))
            .ToList();

        var far = CombatTestData.Combatant(
            "far",
            sideId: CombatTestData.Monsters,
            x: 15,
            y: 15,
            stats: CombatTestData.Stats(maximumHitPoints: 40));

        var encounter = Encounter.Start(
            new Battlefield(20, 20),
            near.Append(far).Prepend(caster),
            new SeededSequence(10, 1, 1, 1, 1, 5, 5, 5, 5, 5, 5, 5, 5));

        Assert.Null(encounter.CastSpell("spell.fireball", new GridPosition(7, 5)));

        Assert.All(near, goblin => Assert.True(goblin.CurrentHitPoints < goblin.Stats.MaximumHitPoints));
        Assert.Equal(far.Stats.MaximumHitPoints, far.CurrentHitPoints);
    }

    [Fact]
    public void ASuccessfulSaveHalvesTheDamage()
    {
        var caster = Caster("caster", [Fireball()], initiative: 10);
        var lucky = CombatTestData.Combatant(
            "lucky",
            sideId: CombatTestData.Monsters,
            x: 6,
            y: 5,
            stats: CombatTestData.Stats(maximumHitPoints: 60));

        // A natural 20 on the save, then damage dice of 4 each.
        var encounter = Encounter.Start(
            new Battlefield(20, 20),
            [caster, lucky],
            new SeededSequence(10, 1, 20, 4, 4, 4, 4, 4, 4, 4, 4));

        Assert.Null(encounter.CastSpell("spell.fireball", new GridPosition(6, 5)));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("halved by a successful save", StringComparison.Ordinal));
    }

    [Fact]
    public void ConcentrationStartsAndIsBrokenByDamage()
    {
        var caster = Caster("caster", [Hold()], initiative: 10, hitPoints: 40);
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(
                initiativeBonus: 0,
                armorClass: 5,
                maximumHitPoints: 60,
                attacks: [CombatTestData.MeleeAttack(bonus: 10, damage: "1d8")]));

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [caster, monster],
            // Initiative, the monster's failed save, Hold's damage, then the monster's
            // attack and damage, and finally the caster's Concentration save on a 1.
            new SeededSequence(10, 1, 10, 4, 7, 5, 1));

        Assert.Null(encounter.CastSpell("spell.hold", monster));
        Assert.Equal("Hold", caster.Features.ConcentratingOn);

        encounter.EndTurn();

        // The monster hits for 7; the caster rolls a natural 1 on the Constitution save.
        Assert.Null(encounter.Attack("Sword", caster));

        Assert.Null(caster.Features.ConcentratingOn);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("loses Concentration", StringComparison.Ordinal));
    }

    [Fact]
    public void ConcentratingOnASecondSpellDropsTheFirst()
    {
        var (encounter, caster, target) = Fight([Hold(), Hold("spell.hold-two", "Hold Two")]);

        Assert.Null(encounter.CastSpell("spell.hold", target));
        Assert.Equal("Hold", caster.Features.ConcentratingOn);

        // Casting takes an Action, so give the caster a fresh turn for the second.
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.Null(encounter.CastSpell("spell.hold-two", target));
        Assert.Equal("Hold Two", caster.Features.ConcentratingOn);
    }

    [Fact]
    public void ASpellTooSlowToCastInAFightIsRefused()
    {
        var ritual = Fireball() with
        {
            Id = "spell.ritual",
            Name = "Ritual",
            CastingTime = SpellCastingTime.Extended,
            CastingTimeText = "10 minutes",
        };

        var (encounter, _, target) = Fight([ritual]);

        Assert.Equal("spell.too_slow", encounter.CastSpell("spell.ritual", target)?.Code);
    }

    [Fact]
    public void ASpellWithNoModelledEffectIsRefusedRatherThanDoingNothing()
    {
        // The rule the whole project runs on: an unimplemented thing says so.
        var inert = Fireball() with
        {
            Id = "spell.inert",
            Name = "Inert",
            Save = null,
            IsSpellAttack = false,
        };

        var (encounter, _, target) = Fight([inert]);

        Assert.Equal("spell.not_implemented", encounter.CastSpell("spell.inert", target)?.Code);
    }

    [Fact]
    public void ASpellTheCasterDoesNotKnowIsRefused()
    {
        var (encounter, _, target) = Fight([FireBolt()]);

        Assert.Equal("spell.unknown", encounter.CastSpell("spell.wish", target)?.Code);
    }

    [Fact]
    public void ANonCasterCannotCast()
    {
        var fighter = CombatTestData.Combatant("fighter", stats: CombatTestData.Stats(initiativeBonus: 10));
        var monster = CombatTestData.Combatant("monster", sideId: CombatTestData.Monsters, x: 1);

        var encounter = Encounter.Start(new Battlefield(8, 8), [fighter, monster], new SeededSequence(10, 1));

        Assert.Equal("spell.not_a_caster", encounter.CastSpell("spell.fire-bolt", monster)?.Code);
    }

    [Fact]
    public void CastingBeyondTheSpellsRangeIsRefused()
    {
        var caster = Caster("caster", [FireBolt()], initiative: 10);
        var distant = CombatTestData.Combatant("distant", sideId: CombatTestData.Monsters, x: 30);

        var encounter = Encounter.Start(new Battlefield(40, 4), [caster, distant], new SeededSequence(10, 1));

        Assert.Equal("spell.out_of_range", encounter.CastSpell("spell.fire-bolt", distant)?.Code);
    }

    private static SpellDefinition FireBolt() => Spell(
        "spell.fire-bolt",
        "Fire Bolt",
        level: 0,
        rangeFeet: 120,
        isAttack: true,
        damage: "1d10");

    private static SpellDefinition MagicBolt() => Spell(
        "spell.magic-bolt",
        "Magic Bolt",
        level: 1,
        rangeFeet: 120,
        isAttack: true,
        damage: "1d10");

    private static SpellDefinition Fireball() => Spell(
        "spell.fireball",
        "Fireball",
        level: 1,
        rangeFeet: 150,
        isAttack: false,
        damage: "8d6",
        area: new EffectArea(AreaShape.Sphere, 20));

    private static SpellDefinition Hold(string id = "spell.hold", string name = "Hold") => Spell(
        id,
        name,
        level: 1,
        rangeFeet: 60,
        isAttack: false,
        damage: "1d4",
        concentration: true);

    private static SpellDefinition Spell(
        string id,
        string name,
        int level,
        int? rangeFeet,
        bool isAttack,
        string damage,
        EffectArea? area = null,
        bool concentration = false)
    {
        var dice = DiceExpression.Parse(damage);
        var components = new[] { new AttackDamage(dice, DamageType.Fire, dice.Average) };

        return new SpellDefinition
        {
            Id = id,
            Name = name,
            Level = level,
            School = MagicSchool.Evocation,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = rangeFeet is null ? "Self" : $"{rangeFeet} feet",
            RangeFeet = rangeFeet,
            Components = SpellComponents.Verbal,
            DurationText = concentration ? "Concentration, up to 1 minute" : "Instantaneous",
            RequiresConcentration = concentration,
            Text = name,
            Mechanics = isAttack ? EntryMechanics.Attack : EntryMechanics.SavingThrow,
            IsSpellAttack = isAttack,
            Damage = components,
            Save = isAttack
                ? null
                : new SaveEffect(Ability.Dexterity, null, area, components, SaveSuccessOutcome.HalfDamage, []),
            SourcePage = 1,
        };
    }

    private static Combatant Caster(
        string id,
        IReadOnlyList<SpellDefinition> spells,
        int initiative,
        int hitPoints = 30)
    {
        var stats = CombatTestData.Stats(
            maximumHitPoints: hitPoints,
            initiativeBonus: initiative,
            diesAtZeroHitPoints: false) with
        {
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 5,
                Spells: spells,
                SpellSlots: new Dictionary<int, int> { [1] = 2, [2] = 1 },
                SpellcastingAbility: Ability.Intelligence,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        return new Combatant(id, id, CombatTestData.Heroes, stats, new GridPosition(0, 0));
    }

    private static (Encounter Encounter, Combatant Caster, Combatant Target) Fight(
        IReadOnlyList<SpellDefinition> spells)
    {
        var caster = Caster("caster", spells, initiative: 10);
        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            x: 2,
            stats: CombatTestData.Stats(armorClass: 5, maximumHitPoints: 80));

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [caster, target],
            new SeededSequence(10, 1, 15, 5, 5, 5, 5, 5, 5, 5, 5, 5));

        return (encounter, caster, target);
    }
}
