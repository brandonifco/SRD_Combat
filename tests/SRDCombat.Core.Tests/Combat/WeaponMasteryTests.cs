using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The four mastery properties this engine executes, against their printed text
/// (page 90).
/// </summary>
public class WeaponMasteryTests
{
    [Fact]
    public void GrazeDealsTheAbilityModifierOnAMiss()
    {
        // "If your attack roll with this weapon misses a creature, you can deal damage
        // to that creature equal to the ability modifier you used to make the attack
        // roll." No dice and no roll — the modifier itself, which is why a miss needs no
        // damage die scripted.
        var (encounter, attacker, target) = Fight(WeaponMastery.Graze, abilityModifier: 3, roll: 2);

        Assert.Equal(20, target.CurrentHitPoints);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);

        Assert.Equal(17, target.CurrentHitPoints);
    }

    [Fact]
    public void GrazeWithNoModifierToSpeakOfDealsNothing()
    {
        var (encounter, attacker, target) = Fight(WeaponMastery.Graze, abilityModifier: 0, roll: 2);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);

        Assert.Equal(20, target.CurrentHitPoints);
    }

    [Fact]
    public void SapLeavesTheTargetSwingingAtDisadvantage()
    {
        // "If you hit a creature with this weapon, that creature has Disadvantage on its
        // next attack roll before the start of your next turn."
        var (encounter, attacker, target) = Fight(
            WeaponMastery.Sap, abilityModifier: 3, roll: 18, extraRolls: [1]);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);

        Assert.Equal(attacker.Id, target.Features.SappedBy);
    }

    [Fact]
    public void ASapIsSpentByTheVeryNextAttackRoll()
    {
        // The sapper's attack and damage, then the victim's attack rolled at
        // Disadvantage (two dice) and its damage.
        var (encounter, attacker, target) = Fight(
            WeaponMastery.Sap, abilityModifier: 3, roll: 18, extraRolls: [1, 15, 14, 1]);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);
        encounter.EndTurn();

        Assert.Equal(attacker.Id, target.Features.SappedBy);

        // The sapped creature swings back: the flag is consumed whether it lands or not.
        encounter.Attack(target.Stats.Attacks[0].Name, attacker);

        Assert.Null(target.Features.SappedBy);
    }

    [Fact]
    public void AnUnspentSapDiesAtTheStartOfTheSappersNextTurn()
    {
        // "before the start of *your* next turn" — the sapper's, not the victim's, which
        // is the possessive this project has got wrong before.
        var (encounter, attacker, target) = Fight(
            WeaponMastery.Sap, abilityModifier: 3, roll: 18, extraRolls: [1]);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);
        encounter.EndTurn();

        Assert.Equal(attacker.Id, target.Features.SappedBy);

        // The victim's whole turn passes without attacking, then the sapper's comes round.
        encounter.EndTurn();

        Assert.Null(target.Features.SappedBy);
    }

    [Fact]
    public void VexBuysAdvantageAgainstThatCreatureOnly()
    {
        // "you have Advantage on your next attack roll against that creature".
        var (encounter, attacker, target) = Fight(
            WeaponMastery.Vex, abilityModifier: 3, roll: 18, extraRolls: [1]);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);

        Assert.Equal(target.Id, attacker.Features.VexedTargetId);
    }

    [Fact]
    public void ToppleKnocksTheTargetProneOnAFailedSave()
    {
        // "a Constitution saving throw (DC 8 plus the ability modifier used to make the
        // attack roll and your Proficiency Bonus)". Modifier 3, proficiency 2 — DC 13.
        Assert.Equal(13, WeaponMasteryRules.ToppleDifficultyClass(abilityModifier: 3, proficiencyBonus: 2));

        // Rolls: two initiatives, the attack, then the save.
        var (encounter, attacker, target) = Fight(
            WeaponMastery.Topple,
            abilityModifier: 3,
            roll: 18,
            extraRolls: [2, 1]);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);

        Assert.True(target.HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void ToppleLeavesAToughTargetStanding()
    {
        var (encounter, attacker, target) = Fight(
            WeaponMastery.Topple,
            abilityModifier: 3,
            roll: 18,
            extraRolls: [19, 1]);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);

        Assert.False(target.HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void AnUnmasteredWeaponDoesNoneOfIt()
    {
        // The property is "usable only by a character who has a feature ... that unlocks
        // the property", so a weapon whose mastery was never unlocked carries none.
        var (encounter, attacker, target) = Fight(
            mastery: null, abilityModifier: 3, roll: 18, extraRolls: [1]);

        encounter.Attack(attacker.Stats.Attacks[0].Name, target);

        Assert.Null(target.Features.SappedBy);
        Assert.Null(attacker.Features.VexedTargetId);
        Assert.False(target.HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void OnlyFourOfTheEightPropertiesAreExecuted()
    {
        // The allowlist, stated: the other four need machinery that does not exist, and
        // the reasons are on WeaponMasteryRules.
        Assert.Equal(
            [WeaponMastery.Graze, WeaponMastery.Sap, WeaponMastery.Topple, WeaponMastery.Vex],
            WeaponMasteryRules.Executed.OrderBy(mastery => mastery));

        foreach (var absent in new[]
                 {
                     WeaponMastery.Cleave, WeaponMastery.Nick, WeaponMastery.Push, WeaponMastery.Slow,
                 })
        {
            Assert.False(WeaponMasteryRules.Executes(absent));
        }
    }

    /// <summary>
    /// An attacker who acts first and a target that can swing back, with the attack roll
    /// scripted. The first two dice are the initiative rolls.
    /// </summary>
    private static (Encounter Encounter, Combatant Attacker, Combatant Target) Fight(
        WeaponMastery? mastery,
        int abilityModifier,
        int roll,
        IReadOnlyList<int>? extraRolls = null)
    {
        var attack = CombatTestData.MeleeAttack(bonus: 5, damage: "1d1") with
        {
            Mastery = mastery,
            AbilityModifier = abilityModifier,
        };

        var attacker = CombatTestData.Combatant(
            "attacker",
            stats: CombatTestData.Stats(initiativeBonus: 10, attacks: [attack]));

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                armorClass: 12,
                maximumHitPoints: 20,
                initiativeBonus: -10,
                attacks: [CombatTestData.MeleeAttack(bonus: 5, damage: "1d1")]),
            x: 1);

        // Initiative twice, the attack roll, then whatever the test says follows. The
        // order inside an attack is roll, then Topple's save, then damage — Sap and
        // Topple land on the hit itself, before any damage is rolled.
        int[] dice = [20, 1, roll, .. extraRolls ?? []];

        return (
            Encounter.Start(new Battlefield(12, 12), [attacker, target], new ScriptedRandomSource(dice)),
            attacker,
            target);
    }
}
