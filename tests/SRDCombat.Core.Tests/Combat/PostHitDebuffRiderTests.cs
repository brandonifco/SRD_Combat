using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Shape 3 of the #390 ledger (#665): printed post-hit debuff riders that ride a
/// monster attack or a monster's saving-throw entry directly, rather than the Sap/
/// Slow Weapon Mastery properties <c>WeaponMasteryTests</c> pins. Both riders here are
/// hand-authored fixtures — the content half, that the real Ettin and Steam Mephit
/// actually carry these fields, is <c>PostHitDebuffRiderContentTests</c> in
/// <c>SRDCombat.Content.Tests</c>.
/// </summary>
/// <remarks>
/// The whole point of this slice is that these two riders are printed on a
/// <em>different</em> clock than the mastery properties they resemble — see
/// <see cref="Definitions.MonsterAttack.ImposesDisadvantageOnTargetsNextAttack"/> and
/// <see cref="Definitions.SaveEffect.TargetSpeedDecreaseFeet"/>'s doc comments for the
/// printed wording each reading rests on. Getting the clock wrong moves the expiry by
/// most of a round without anything failing loudly, so every timing test below proves
/// the boundary lands on the *correct* creature's turn, not merely that it lands
/// eventually.
/// </remarks>
public class PostHitDebuffRiderTests
{
    // ---- The Ettin's Morningstar: Disadvantage on the bearer's own next attack roll ----

    [Fact]
    public void AHitLeavesTheTargetDisadvantagedOnItsNextAttackRoll()
    {
        // One extra die for the attack's own 1d1 damage roll, consumed on the hit.
        var (encounter, attacker, _, target) = DisadvantageFight(hitRoll: 18, extraRolls: [1]);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.True(target.Features.NextAttackDisadvantaged);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Disadvantage on its next attack roll", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissLeavesNoRider()
    {
        // The rider reads "Hit: ... and the target has Disadvantage", so it lands on
        // the hit itself, exactly like Sap — a miss carries nothing.
        var (encounter, attacker, _, target) = DisadvantageFight(hitRoll: 2);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));

        Assert.False(target.Features.NextAttackDisadvantaged);
    }

    [Fact]
    public void TheDisadvantageIsSpentByTheBearersVeryNextAttackRollHoweverItLands()
    {
        // Attacker hits (rider lands, one die for its own 1d1 damage), the turn order
        // cycles round to the target, and the target swings back at Disadvantage —
        // two dice for that roll (the lower, 10, still clears AC 13 against the +5
        // bonus) plus one for its damage. The script is exact: a surplus or missing
        // die throws, which is the proof the roll really was made twice.
        var (encounter, attacker, filler, target) = DisadvantageFight(
            hitRoll: 18,
            extraRolls: [1, 15, 10, 1]);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));
        encounter.EndTurn(); // ends attacker's turn, begins filler's
        encounter.EndTurn(); // ends filler's turn, begins target's

        Assert.True(target.Features.NextAttackDisadvantaged);

        Assert.Null(encounter.Attack(target.Stats.Attacks[0].Name, attacker));

        Assert.False(target.Features.NextAttackDisadvantaged);
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack
                && step.ActorId == target.Id
                && step.Narration.Contains("with Disadvantage", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnspentDisadvantageDiesAtTheEndOfTheBearersOwnNextTurnNotTheAttackers()
    {
        // Three combatants, deliberately ordered attacker > target > filler, so the
        // attacker's own next turn (its clock, which the printed Sap property would
        // run on) begins only *after* the target's own next turn has already ended.
        // If this rider were wrongly wired to the attacker's clock (Sap's shape), it
        // would still be sitting there when the target's turn ends, because the
        // attacker will not have begun a new turn yet.
        var (encounter, attacker, target, filler) = DisadvantageFight(
            hitRoll: 18,
            extraRolls: [1],
            order: DisadvantageFightOrder.AttackerTargetFiller);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, target));
        Assert.Equal(1, attacker.TurnsBegun);
        Assert.Equal(0, target.TurnsBegun);

        encounter.EndTurn(); // ends the attacker's turn 1, begins the target's turn 1
        Assert.Equal(1, target.TurnsBegun);
        Assert.True(target.Features.NextAttackDisadvantaged, "still mid-target's-own-turn: not cleared at its start.");

        encounter.EndTurn(); // ends the target's own turn 1 (its "next turn" completes)

        // The attacker has not begun turn 2 yet — proof this cleared on the bearer's
        // clock, not the attacker's.
        Assert.Equal(1, attacker.TurnsBegun);
        Assert.False(target.Features.NextAttackDisadvantaged);
    }

    private enum DisadvantageFightOrder
    {
        AttackerFillerTarget,
        AttackerTargetFiller,
    }

    /// <summary>
    /// An attacker whose one attack imposes the Ettin's Morningstar rider, a target,
    /// and a filler combatant that separates the attacker's own turn boundary from
    /// the target's — see <see cref="AnUnspentDisadvantageDiesAtTheEndOfTheBearersOwnNextTurnNotTheAttackers"/>
    /// for why the separation matters. Dice: three initiative rolls (attacker highest,
    /// filler middle, target lowest, in whichever seating <paramref name="order"/>
    /// asks for), the attack roll, then whatever the test supplies.
    /// </summary>
    private static (Encounter Encounter, Combatant Attacker, Combatant Second, Combatant Third) DisadvantageFight(
        int hitRoll,
        IReadOnlyList<int>? extraRolls = null,
        DisadvantageFightOrder order = DisadvantageFightOrder.AttackerFillerTarget)
    {
        var attack = CombatTestData.MeleeAttack(bonus: 5, damage: "1d1") with
        {
            ImposesDisadvantageOnTargetsNextAttack = true,
        };

        var attacker = CombatTestData.Combatant(
            "attacker", stats: CombatTestData.Stats(initiativeBonus: 20, attacks: [attack]));

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                armorClass: 12,
                maximumHitPoints: 20,
                initiativeBonus: order == DisadvantageFightOrder.AttackerFillerTarget ? -10 : 5,
                attacks: [CombatTestData.MeleeAttack(bonus: 5, damage: "1d1")]),
            x: 1);

        var filler = CombatTestData.Combatant(
            "filler",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                initiativeBonus: order == DisadvantageFightOrder.AttackerFillerTarget ? 5 : -10,
                attacks: []),
            x: 2);

        int[] dice = [20, 1, 1, hitRoll, .. extraRolls ?? []];

        var combatants = order == DisadvantageFightOrder.AttackerFillerTarget
            ? new[] { attacker, filler, target }
            : [attacker, target, filler];

        var encounter = Encounter.Start(new Battlefield(12, 12), combatants, new ScriptedRandomSource(dice));

        return order == DisadvantageFightOrder.AttackerFillerTarget
            ? (encounter, attacker, filler, target)
            : (encounter, attacker, target, filler);
    }

    // ---- The Steam Mephit's Steam Breath: Speed decrease on the imposer's own clock ----

    [Fact]
    public void AFailedSaveReducesTheVictimsSpeedByThePrintedAmount()
    {
        var (encounter, source, victim, _) = SpeedDecreaseFight(saveRoll: 1);

        Assert.Null(encounter.UseEntry("Steam Breath", victim));

        Assert.True(victim.Features.SpeedDecreasedBy.ContainsKey(source.Id));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Speed reduced by 10 feet", StringComparison.Ordinal));

        // The flag alone is not the printed effect — the reduction has to actually
        // reach the movement budget the victim's own turn hands out.
        // EffectiveSpeedFeet is only consulted when a turn *begins*, so the proof is
        // the victim's own next turn starting 10 feet short of its printed 30.
        encounter.EndTurn(); // ends the source's turn, begins the victim's
        Assert.Equal(20, victim.Turn.MovementFeet);
    }

    [Fact]
    public void ASuccessfulSaveTakesNoSpeedDecrease()
    {
        var (encounter, source, victim, _) = SpeedDecreaseFight(saveRoll: 20);

        Assert.Null(encounter.UseEntry("Steam Breath", victim));

        Assert.False(victim.Features.SpeedDecreasedBy.ContainsKey(source.Id));
    }

    [Fact]
    public void AnUnspentSpeedDecreaseDiesAtTheEndOfTheImposersOwnNextTurnNotTheVictims()
    {
        // Ordered source > victim > filler, the mirror image of the Disadvantage
        // rider's ordering: this time the printed clock belongs to the imposer, so the
        // rider must survive the *victim's* own next turn ending and clear only when
        // the source's own next turn ends.
        var (encounter, source, victim, filler) = SpeedDecreaseFight(saveRoll: 1);

        Assert.Null(encounter.UseEntry("Steam Breath", victim));
        Assert.Equal(1, source.TurnsBegun);

        encounter.EndTurn(); // ends the source's turn 1, begins the victim's turn 1
        Assert.True(victim.Features.SpeedDecreasedBy.ContainsKey(source.Id));
        Assert.Equal(20, victim.Turn.MovementFeet);

        encounter.EndTurn(); // ends the victim's own next turn — the rider must still hold
        Assert.True(
            victim.Features.SpeedDecreasedBy.ContainsKey(source.Id),
            "the victim's own turn ending must not clear a rider stamped on the imposer's clock.");

        encounter.EndTurn(); // ends the filler's turn, begins the source's own turn 2
        Assert.Equal(2, source.TurnsBegun);
        Assert.True(
            victim.Features.SpeedDecreasedBy.ContainsKey(source.Id),
            "the source's turn 2 has only just begun — its own next turn has not yet ended.");

        encounter.EndTurn(); // ends the source's own turn 2 — its "next turn" completes,
                              // and cascades into the victim's own turn 2 beginning
        Assert.False(victim.Features.SpeedDecreasedBy.ContainsKey(source.Id));
        Assert.Equal(30, victim.Turn.MovementFeet);
    }

    /// <summary>
    /// A mephit-shaped source with a single-target Steam Breath-style save entry, a
    /// victim, and a filler combatant seated between the victim and the source's own
    /// next turn. Dice: three initiative rolls (source highest), then the save roll.
    /// </summary>
    private static (Encounter Encounter, Combatant Source, Combatant Victim, Combatant Filler) SpeedDecreaseFight(
        int saveRoll)
    {
        var save = new SaveEffect(
            Ability.Constitution,
            10,
            Area: null,
            FailureDamage: [],
            SaveSuccessOutcome.NoEffect,
            AppliedConditions: [],
            TargetSpeedDecreaseFeet: 10);

        var stats = CombatTestData.Stats(initiativeBonus: 20, attacks: []) with
        {
            Entries = [new MonsterEntry("Steam Breath", MonsterEntrySection.Action, "Steam Breath.", Mechanics: EntryMechanics.SavingThrow, Save: save)],
        };

        var source = CombatTestData.Combatant("source", sideId: CombatTestData.Monsters, stats: stats);

        // Seated ahead of the filler on purpose — see the class remarks on
        // AnUnspentSpeedDecreaseDiesAtTheEndOfTheImposersOwnNextTurnNotTheVictims:
        // the victim's own next turn must end *before* the source's does, so the two
        // clocks are distinguishable.
        var victim = CombatTestData.Combatant(
            "victim",
            stats: CombatTestData.Stats(maximumHitPoints: 20, initiativeBonus: 5, speedFeet: 30, attacks: []),
            x: 1);

        var filler = CombatTestData.Combatant(
            "filler",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, attacks: []),
            x: 2);

        int[] dice = [20, 1, 1, saveRoll];

        var encounter = Encounter.Start(
            new Battlefield(12, 12), [source, victim, filler], new ScriptedRandomSource(dice));

        return (encounter, source, victim, filler);
    }
}
