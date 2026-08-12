using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Passive monster traits: the curated registry, and the three effects it executes.
/// </summary>
/// <remarks>
/// The rules text these pin: Pack Tactics — "The wolf has Advantage on an attack roll
/// against a creature if at least one of the wolf's allies is within 5 feet of the
/// creature and the ally doesn't have the Incapacitated condition." Magic Resistance —
/// "The creature has Advantage on saving throws against spells and other magical
/// effects." Flyby — "The creature doesn't provoke Opportunity Attacks when it flies
/// out of an enemy's reach." The scripted die is the proof device throughout: Advantage
/// consumes two d20s and a normal roll one, so the script itself asserts which mode was
/// rolled.
/// </remarks>
public class MonsterTraitTests
{
    [Fact]
    public void TheRegistryReadsOnlyTraitEntriesItImplements()
    {
        var traits = MonsterTraitRegistry.TraitsOf(
        [
            new MonsterEntry("Pack Tactics", MonsterEntrySection.Trait, "..."),
            new MonsterEntry("Flyby", MonsterEntrySection.Action, "..."),
            new MonsterEntry("Keen Smell", MonsterEntrySection.Trait, "..."),
        ]);

        // Pack Tactics registers; "Flyby" printed under Actions is not the trait; Keen
        // Smell is not implemented and stays counted rather than quietly inert.
        Assert.Equal([MonsterTrait.PackTactics], traits);
    }

    [Fact]
    public void PackTacticsGrantsAdvantageWithAnAllyBesideTheTarget()
    {
        // Two d20s are scripted for the attack: 10 and 3. Advantage takes the 10, and
        // the script would throw if only one had been consumed before the damage die.
        var (encounter, _, _) = WolfPack(new ScriptedRandomSource(20, 1, 1, 10, 3, 1), packmateBesideTarget: true);

        Assert.Null(encounter.Attack("Bite", Target(encounter)));

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack
                && step.Narration.Contains("with Advantage", StringComparison.Ordinal));
    }

    [Fact]
    public void PackTacticsAloneIsANormalAttack()
    {
        // The packmate is across the field: one d20, no Advantage note.
        var (encounter, _, _) = WolfPack(new ScriptedRandomSource(20, 1, 1, 10, 1), packmateBesideTarget: false);

        Assert.Null(encounter.Attack("Bite", Target(encounter)));

        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("with Advantage", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIncapacitatedAllyLendsNoPackTactics()
    {
        var (encounter, _, packmate) = WolfPack(
            new ScriptedRandomSource(20, 1, 1, 10, 1),
            packmateBesideTarget: true);

        packmate.AddCondition(ConditionType.Incapacitated);

        Assert.Null(encounter.Attack("Bite", Target(encounter)));

        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("with Advantage", StringComparison.Ordinal));
    }

    [Fact]
    public void PackTacticsCancelsAgainstTheAttackersOwnPoison()
    {
        // Advantage and Disadvantage cancel rather than stack: Pack Tactics plus the
        // attacker's own Poisoned is one flat d20.
        var (encounter, wolf, _) = WolfPack(
            new ScriptedRandomSource(20, 1, 1, 10, 1),
            packmateBesideTarget: true);

        wolf.AddCondition(ConditionType.Poisoned);

        Assert.Null(encounter.Attack("Bite", Target(encounter)));

        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("with Advantage", StringComparison.Ordinal)
                || step.Narration.Contains("with Disadvantage", StringComparison.Ordinal));
    }

    [Fact]
    public void FlybyLeavesReachWithoutProvoking()
    {
        var (encounter, flyer) = FlyerFight(withFlyby: true, new ScriptedRandomSource(20, 1));

        Assert.Null(encounter.Move(new GridPosition(5, 5)));

        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
        Assert.Equal(new GridPosition(5, 5), flyer.Position);
    }

    [Fact]
    public void TheSameMoveWithoutFlybyProvokes()
    {
        var (encounter, _) = FlyerFight(withFlyby: false, new ScriptedRandomSource(20, 1, 10, 1));

        Assert.Null(encounter.Move(new GridPosition(5, 5)));

        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
    }

    [Fact]
    public void MagicResistanceIsAdvantageAgainstASpell()
    {
        // The two scripted d20s disagree: 3 fails against DC 13 and 18 succeeds.
        // "success." in the narration proves the higher die — Advantage — was used.
        var (encounter, resister) = SpellFight(new ScriptedRandomSource(20, 1, 3, 18, 1));

        Assert.Null(encounter.CastSpell("spell.mind-lance", resister));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("saving throw", StringComparison.Ordinal)
                && step.Narration.Contains("success.", StringComparison.Ordinal));
    }

    [Fact]
    public void MagicResistanceIsNotAdvantageAgainstABreathWeapon()
    {
        // A stat block's save entry is read as not magical — recorded on the registry.
        // One d20 (3, a failure against DC 12) and one damage die: a second d20 here
        // would make the script throw on the damage roll.
        var breather = CombatTestData.Combatant(
            "breather",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
            {
                Entries =
                [
                    new MonsterEntry("Acid Breath", MonsterEntrySection.Action, "Breathes acid.",
                        Mechanics: EntryMechanics.SavingThrow,
                        Save: new SaveEffect(
                            Ability.Dexterity,
                            12,
                            null,
                            [new AttackDamage(DiceExpression.Parse("1d6"), DamageType.Acid, 3)],
                            SaveSuccessOutcome.HalfDamage,
                            [])),
                ],
            },
            y: 5);

        var resister = Resister("resister", x: 1);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [breather, resister],
            new ScriptedRandomSource(20, 1, 3, 1));

        Assert.Null(encounter.UseEntry("Acid Breath", resister));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("saving throw", StringComparison.Ordinal)
                && step.Narration.Contains("failure", StringComparison.Ordinal));
    }

    /// <summary>Two wolves and a target: the attacker at (0,5), the target at (1,5).</summary>
    private static (Encounter Encounter, Combatant Wolf, Combatant Packmate) WolfPack(
        IRandomSource random,
        bool packmateBesideTarget)
    {
        var bite = CombatTestData.MeleeAttack("Bite", bonus: 4, damage: "1d6 + 2");

        CombatantStats WolfStats(int initiative) =>
            CombatTestData.Stats(initiativeBonus: initiative, attacks: [bite]) with
            {
                Entries = [new MonsterEntry("Pack Tactics", MonsterEntrySection.Trait, "...")],
            };

        var wolf = CombatTestData.Combatant("wolf", sideId: CombatTestData.Monsters, stats: WolfStats(10), y: 5);
        var packmate = CombatTestData.Combatant(
            "packmate",
            sideId: CombatTestData.Monsters,
            stats: WolfStats(-10),
            x: packmateBesideTarget ? 1 : 8,
            y: packmateBesideTarget ? 4 : 0);
        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, attacks: []),
            x: 1,
            y: 5);

        return (Encounter.Start(new Battlefield(12, 12), [wolf, packmate, target], random), wolf, packmate);
    }

    private static Combatant Target(Encounter encounter) =>
        encounter.Combatants.Single(combatant => combatant.Id == "target");

    /// <summary>A mover beside an armed enemy, with or without the Flyby trait.</summary>
    private static (Encounter Encounter, Combatant Flyer) FlyerFight(bool withFlyby, IRandomSource random)
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries = withFlyby ? [new MonsterEntry("Flyby", MonsterEntrySection.Trait, "...")] : [],
        };

        var flyer = CombatTestData.Combatant("flyer", sideId: CombatTestData.Monsters, stats: stats, x: 1, y: 5);
        var guard = CombatTestData.Combatant(
            "guard",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            y: 5);

        var encounter = Encounter.Start(new Battlefield(12, 12), [flyer, guard], random);

        return (encounter, flyer);
    }

    /// <summary>A caster with one single-target save spell, and a Magic Resistance monster.</summary>
    private static (Encounter Encounter, Combatant Resister) SpellFight(IRandomSource random)
    {
        var dice = DiceExpression.Parse("1d4");

        var spell = new SpellDefinition
        {
            Id = "spell.mind-lance",
            Name = "Mind Lance",
            Level = 1,
            School = MagicSchool.Evocation,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "60 feet",
            RangeFeet = 60,
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Text = "Mind Lance",
            Mechanics = EntryMechanics.SavingThrow,
            IsSpellAttack = false,
            Damage = [new AttackDamage(dice, DamageType.Psychic, dice.Average)],
            Save = new SaveEffect(
                Ability.Dexterity,
                null,
                null,
                [new AttackDamage(dice, DamageType.Psychic, dice.Average)],
                SaveSuccessOutcome.HalfDamage,
                []),
            SourcePage = 1,
        };

        var caster = CombatTestData.Combatant(
            "caster",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(initiativeBonus: 10, diesAtZeroHitPoints: false) with
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
                    Spells: [spell],
                    SpellSlots: new Dictionary<int, int> { [1] = 2 },
                    SpellcastingAbility: Ability.Intelligence,
                    SpellSaveDifficultyClass: 13,
                    SpellAttackBonus: 5),
            });

        var resister = Resister("resister", x: 2);

        return (Encounter.Start(new Battlefield(12, 12), [caster, resister], random), resister);
    }

    /// <summary>A monster whose stat block prints Magic Resistance.</summary>
    private static Combatant Resister(string id, int x) =>
        CombatTestData.Combatant(
            id,
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, attacks: []) with
            {
                Entries = [new MonsterEntry("Magic Resistance", MonsterEntrySection.Trait, "...")],
            },
            x: x);
}
