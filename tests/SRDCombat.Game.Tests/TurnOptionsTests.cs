using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Which actions a client offers, and the keys they answer to.
/// </summary>
/// <remarks>
/// The class mirrors the engine's refusals so a button for something unusable is never
/// drawn, and that duplication is the thing worth guarding: the tests below check the
/// direction that can actually hurt a player — that nothing usable is hidden — and that
/// a key belongs to an action rather than to a place in the row.
/// </remarks>
public class TurnOptionsTests
{
    [Fact]
    public void EveryActionHasItsOwnKey()
    {
        // "D is always Dodge" only holds if no two actions ever want D. Unique across
        // the whole set means the assignment never has to be recomputed, so nothing is
        // relearned when the row changes or the character does.
        var keys = Enum.GetValues<TurnAction>().Select(TurnOptions.Hotkey).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal('D', TurnOptions.Hotkey(TurnAction.Dodge));
        Assert.DoesNotContain('\0', keys);
    }

    [Fact]
    public void SpendingTheActionTakesTheActionButtonsAway()
    {
        var (encounter, fighter) = Fight();

        Assert.Contains(TurnAction.Dodge, TurnOptions.For(encounter, fighter));
        Assert.Contains(TurnAction.Dash, TurnOptions.For(encounter, fighter));

        Assert.Null(encounter.Dodge());

        Assert.DoesNotContain(TurnAction.Dodge, TurnOptions.For(encounter, fighter));
        Assert.DoesNotContain(TurnAction.Dash, TurnOptions.For(encounter, fighter));

        // End Turn survives everything: it is the one way out of every state.
        Assert.Contains(TurnAction.EndTurn, TurnOptions.For(encounter, fighter));
    }

    [Fact]
    public void SpendingTheBonusActionTakesTheBonusButtonsAway()
    {
        var (encounter, fighter) = Fight();

        Assert.Contains(TurnAction.SecondWind, TurnOptions.For(encounter, fighter));

        Assert.Null(encounter.SecondWind());

        Assert.DoesNotContain(TurnAction.SecondWind, TurnOptions.For(encounter, fighter));
    }

    [Fact]
    public void ActionSurgeAppearsOnlyOnceThereIsNoActionLeft()
    {
        // It buys an Action, so offering it while one is in hand is offering a waste —
        // which is exactly what the engine refuses it for.
        var (encounter, fighter) = Fight();

        Assert.DoesNotContain(TurnAction.ActionSurge, TurnOptions.For(encounter, fighter));
        Assert.Equal("feature.action_surge.action_available", encounter.ActionSurge()?.Code);

        Assert.Null(encounter.Dodge());

        Assert.Contains(TurnAction.ActionSurge, TurnOptions.For(encounter, fighter));
        Assert.Null(encounter.ActionSurge());
    }

    [Fact]
    public void CastGoesAwayOnceTheActionIsSpent()
    {
        // Found in play: asking "is either action free?" left Cast on the row after an
        // Action spell, because the Bonus Action was still in hand. It opened the list
        // and met "action.spent" on whatever was picked.
        var (encounter, cleric) = CasterFight();

        Assert.Contains(TurnAction.Cast, TurnOptions.For(encounter, cleric));

        Assert.Null(encounter.Dodge());

        Assert.DoesNotContain(TurnAction.Cast, TurnOptions.For(encounter, cleric));
    }

    [Fact]
    public void ABonusActionSpellKeepsCastAliveAfterTheAction()
    {
        // The other half of the same rule: Healing Word is a Bonus Action, so spending
        // the Action must not take the whole menu away.
        var (encounter, cleric) = CasterFight(bonusActionSpell: true);

        Assert.Null(encounter.Dodge());

        Assert.Contains(TurnAction.Cast, TurnOptions.For(encounter, cleric));
    }

    [Fact]
    public void ASlottedSpellWithNoSlotLeftIsNotCastable()
    {
        var (encounter, cleric) = CasterFight(slots: 0);

        Assert.DoesNotContain(TurnAction.Cast, TurnOptions.For(encounter, cleric));

        // And the predicate says so per spell, which is what filters the menu.
        Assert.All(
            cleric.Stats.Character!.Spells,
            spell => Assert.False(TurnOptions.CanCastNow(cleric, spell)));
    }

    [Fact]
    public void StandUpIsOfferedOnlyWhileProne()
    {
        var (encounter, fighter) = Fight();

        Assert.DoesNotContain(TurnAction.StandUp, TurnOptions.For(encounter, fighter));
        Assert.Equal("combatant.not_prone", encounter.StandUp()?.Code);

        fighter.AddCondition(ConditionType.Prone);

        Assert.Contains(TurnAction.StandUp, TurnOptions.For(encounter, fighter));
    }

    [Fact]
    public void EscapeIsOfferedOnlyWhileGrappled()
    {
        var (encounter, fighter) = Fight();

        Assert.DoesNotContain(TurnAction.Escape, TurnOptions.For(encounter, fighter));
        Assert.Equal("combatant.not_grappled", encounter.Escape()?.Code);
    }

    [Fact]
    public void EverythingHiddenIsAlsoRefusedByTheEngine()
    {
        // The invariant the duplication has to hold to: an action this class hides is
        // one the engine would have refused anyway, so nothing usable is ever taken off
        // the row. Checked with the turn fresh and again with it spent.
        var (encounter, fighter) = Fight();

        AssertHiddenAreRefused(encounter, fighter);

        Assert.Null(encounter.Dodge());
        Assert.Null(encounter.SecondWind());

        AssertHiddenAreRefused(encounter, fighter);
    }

    private static void AssertHiddenAreRefused(Encounter encounter, Combatant actor)
    {
        var offered = TurnOptions.For(encounter, actor).ToHashSet();

        // Only the actions this fighter could ever take are worth asserting on; the
        // rest are absent because the character has no such feature at all.
        foreach (var action in new[] { TurnAction.Dodge, TurnAction.Dash, TurnAction.Disengage, TurnAction.SecondWind })
        {
            if (offered.Contains(action))
            {
                continue;
            }

            var refusal = action switch
            {
                TurnAction.Dodge => encounter.Dodge(),
                TurnAction.Dash => encounter.Dash(),
                TurnAction.Disengage => encounter.Disengage(),
                TurnAction.SecondWind => encounter.SecondWind(),
                _ => null,
            };

            Assert.True(refusal is not null, $"{action} was hidden but the engine allowed it");
        }
    }

    /// <summary>A Cleric carrying one slotted spell, acting first.</summary>
    private static (Encounter Encounter, Combatant Cleric) CasterFight(
        bool bonusActionSpell = false,
        int slots = 2)
    {
        var abilities = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => new MonsterAbility(12, 1));

        var spell = new SpellDefinition
        {
            Id = "spell.test-bolt",
            Name = "Test Bolt",
            Level = 1,
            School = MagicSchool.Evocation,
            Classes = ["Cleric"],
            CastingTime = bonusActionSpell ? SpellCastingTime.BonusAction : SpellCastingTime.Action,
            CastingTimeText = "Action",
            RangeText = "60 feet",
            RangeFeet = 60,
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Text = "Test Bolt",
            Mechanics = EntryMechanics.Attack,
            IsSpellAttack = true,
            Damage = [new AttackDamage(DiceExpression.Parse("1d8"), DamageType.Radiant, 4)],
            SourcePage = 1,
        };

        var stats = new CombatantStats(
            16, 30, 30, 10, abilities, 2, CreatureSize.Medium,
            new Dictionary<DamageType, DamageResponse>(), [],
            [new CombatAttack("Mace", AttackKind.Melee, 4, 5, null, null,
                [new AttackDamage(DiceExpression.Parse("1d6 + 1"), DamageType.Bludgeoning, 4)])],
            DiesAtZeroHitPoints: false)
        {
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3,
                Spells: [spell],
                SpellSlots: new Dictionary<int, int> { [1] = slots },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var cleric = new Combatant("aldous", "Aldous", "party", stats, new GridPosition(0, 0));

        var monster = new Combatant(
            "goblin",
            "Goblin",
            "monsters",
            new CombatantStats(
                13, 20, 30, -10, abilities, 2, CreatureSize.Medium,
                new Dictionary<DamageType, DamageResponse>(), [],
                [new CombatAttack("Scimitar", AttackKind.Melee, 4, 5, null, null,
                    [new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Slashing, 5)])],
                DiesAtZeroHitPoints: true),
            new GridPosition(6, 0));

        return (Encounter.Start(new Battlefield(10, 8), [cleric, monster], new SeededRandomSource(3)), cleric);
    }

    /// <summary>A Fighter with Second Wind and Action Surge, acting first.</summary>
    private static (Encounter Encounter, Combatant Fighter) Fight()
    {
        var abilities = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => new MonsterAbility(12, 1));

        var stats = new CombatantStats(
            16, 30, 30, 10, abilities, 2, CreatureSize.Medium,
            new Dictionary<DamageType, DamageResponse>(), [],
            [new CombatAttack("Longsword", AttackKind.Melee, 5, 5, null, null,
                [new AttackDamage(DiceExpression.Parse("1d8 + 3"), DamageType.Slashing, 7)])],
            DiesAtZeroHitPoints: false)
        {
            Character = new CombatantFeatures(
                [ClassFeature.SecondWind, ClassFeature.ActionSurge],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 2,
                ActionSurgeUses: 1,
                Level: 3),
        };

        var fighter = new Combatant("brenna", "Brenna", "party", stats, new GridPosition(0, 0));

        var monster = new Combatant(
            "goblin",
            "Goblin",
            "monsters",
            new CombatantStats(
                13, 20, 30, -10, abilities, 2, CreatureSize.Medium,
                new Dictionary<DamageType, DamageResponse>(), [],
                [new CombatAttack("Scimitar", AttackKind.Melee, 4, 5, null, null,
                    [new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Slashing, 5)])],
                DiesAtZeroHitPoints: true),
            new GridPosition(6, 0));

        var encounter = Encounter.Start(
            new Battlefield(10, 8),
            [fighter, monster],
            new SeededRandomSource(3));

        return (encounter, fighter);
    }
}
