using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Glossary p.186's Concentration clause: "Incapacitated or Dead. Your Concentration
/// ends if you have the Incapacitated condition or you die." Issue #289 —
/// <see cref="Encounter.CheckConcentration"/> was reachable only from the damage paths,
/// so a caster Paralyzed, Stunned or Petrified by a failed save that carries no damage
/// (Hold Person's rider, a two-tier gaze's escalation) kept concentrating regardless.
/// </summary>
/// <remarks>
/// These pin both halves: the no-damage paths that now call
/// <see cref="Encounter.BreakConcentrationOnIncapacitated"/> — a save-imposed rider
/// through <c>ImposeConditions</c>, and an escalating condition through
/// <c>Escalate</c> — and the pre-existing damage path
/// (<see cref="Encounter.CheckConcentration"/>'s own <c>!CanAct</c> shortcut), left
/// unchanged and re-pinned here rather than only inferred from the older
/// <c>CastingTests.ConcentrationStartsAndIsBrokenByDamage</c>, which never actually
/// downs its caster. Every stage uses a <see cref="ScriptedRandomSource"/> sized to
/// exactly the rolls the intended path consumes, so a stray Constitution save — the
/// wrong branch firing — throws instead of passing silently.
/// </remarks>
public class ConcentrationIncapacitatedTests
{
    [Fact]
    public void ASaveImposedParalyzedEndsConcentrationWithNoSaveToRoll()
    {
        // Initiative ×2, Ward's spell attack roll (always a miss against AC 30), then
        // Grip's save roll for the caster (always a failure against DC 30). Exactly
        // four rolls: a stray Constitution save on top would throw.
        var (encounter, caster, gripper) = Stage(new ScriptedRandomSource(15, 1, 10, 10));

        // The caster's own turn: cast the concentration spell at the gripper.
        Assert.Null(encounter.CastSpell("spell.ward", gripper));
        Assert.Equal("Ward", caster.Features.ConcentratingOn);

        encounter.EndTurn();

        // The gripper's turn: Grip is a single-target save entry with no damage and a
        // DC no roll can meet, so the caster fails and is Paralyzed with no
        // Constitution save of its own ever rolled.
        Assert.Null(encounter.UseEntry("Grip", caster));

        Assert.True(caster.HasCondition(ConditionType.Paralyzed));
        Assert.True(caster.HasCondition(ConditionType.Incapacitated));
        Assert.Null(caster.Features.ConcentratingOn);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("caster loses Concentration on Ward", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEscalatingConditionEndsConcentrationOnceItReachesPetrified()
    {
        // Initiative ×2 (the caster first), Ward's attack roll (a miss), the gaze's
        // failed save (Restrained lands — no Incapacitated yet, so Concentration is
        // untouched), then the repeated save at the end of the caster's own next
        // turn, which also fails: Restrained escalates to Petrified, and this is the
        // Escalate path's own call to BreakConcentrationOnIncapacitated, not
        // ImposeConditions's. Exactly five rolls: a Constitution save from
        // CheckConcentration on top would throw.
        var (encounter, caster, gazer) = GazeStage(new ScriptedRandomSource(15, 1, 10, 1, 1));

        Assert.Null(encounter.CastSpell("spell.ward", gazer));
        Assert.Equal("Ward", caster.Features.ConcentratingOn);

        encounter.EndTurn(); // The caster's turn ends; the gazer's begins.

        Assert.Null(encounter.UseEntry("Petrifying Gaze", caster.Position));
        Assert.True(caster.HasCondition(ConditionType.Restrained));
        Assert.False(caster.HasCondition(ConditionType.Incapacitated));

        // Restrained alone brings no Incapacitated, so Concentration survives it —
        // the point of failing this save first rather than going straight to a
        // Petrified rider.
        Assert.Equal("Ward", caster.Features.ConcentratingOn);

        encounter.EndTurn(); // The gazer's turn ends; the caster's begins.
        encounter.EndTurn(); // The caster's own turn ends: the repeat save rolls and fails.

        Assert.False(caster.HasCondition(ConditionType.Restrained));
        Assert.True(caster.HasCondition(ConditionType.Petrified));
        Assert.True(caster.HasCondition(ConditionType.Incapacitated));
        Assert.Null(caster.Features.ConcentratingOn);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("caster loses Concentration on Ward", StringComparison.Ordinal));
    }

    [Fact]
    public void DamageThatDownsTheConcentratorStillEndsItWithNoSaveEither()
    {
        // The pre-existing half of the rule, re-pinned here: damage that drops the
        // concentrator to 0 hit points ends Concentration outright through the same
        // !CanAct shortcut, not a Constitution save — CheckConcentration's own branch,
        // untouched by this fix, now shared with BreakConcentrationOnIncapacitated
        // through the extracted EndConcentration helper. Initiative ×2, Ward's spell
        // attack roll (a miss), a claw that always hits (AC 5), then its two damage
        // dice for "2d6 + 5" against 5 hit points. Exactly six rolls: a Constitution
        // save on top would throw.
        var (encounter, caster, gripper) = Stage(
            new ScriptedRandomSource(15, 1, 10, 15, 3, 3),
            casterHitPoints: 5,
            gripperDamage: "2d6 + 5");

        Assert.Null(encounter.CastSpell("spell.ward", gripper));
        Assert.Equal("Ward", caster.Features.ConcentratingOn);

        encounter.EndTurn();

        Assert.Null(encounter.Attack("Claw", caster));

        Assert.True(caster.HasCondition(ConditionType.Unconscious));
        Assert.Null(caster.Features.ConcentratingOn);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("caster loses Concentration on Ward", StringComparison.Ordinal));
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A caster with a hand-built Concentration attack spell ("Ward") and one enemy
    /// ("gripper") armed with a single-target save entry ("Grip") that imposes
    /// Paralyzed with no damage attached and a DC no d20 can meet. The gripper's AC is
    /// high enough that Ward always misses it, so casting it never spends a damage die.
    /// The caster goes first. Dice beyond initiative are whatever each test's own turns
    /// consume.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Gripper) Stage(
        ScriptedRandomSource random,
        int casterHitPoints = 20,
        string gripperDamage = "1d4")
    {
        var casterStats = CombatTestData.Stats(
            armorClass: 5,
            maximumHitPoints: casterHitPoints,
            initiativeBonus: 10,
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
                Level: 3,
                Spells: [Ward()],
                SpellSlots: new Dictionary<int, int> { [1] = 1 },
                SpellcastingAbility: Ability.Intelligence,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, casterStats, new GridPosition(0, 2));

        var grip = new SaveEffect(
            Ability.Constitution,
            30, // No d20 result plus a +2 save bonus reaches this.
            Area: null,
            FailureDamage: [],
            SuccessOutcome: SaveSuccessOutcome.NoEffect,
            AppliedConditions:
            [
                new AppliedCondition(ConditionType.Paralyzed, Duration: ConditionDuration.RepeatSaveUpToOneMinute),
            ]);

        var gripperStats = CombatTestData.Stats(
            armorClass: 30, // Ward's spell attack can never reach this.
            initiativeBonus: -10,
            attacks: [CombatTestData.MeleeAttack("Claw", bonus: 10, damage: gripperDamage)]) with
        {
            Entries =
            [
                new MonsterEntry("Grip", MonsterEntrySection.Action, "Grip.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: grip),
            ],
        };

        var gripper = CombatTestData.Combatant("gripper", sideId: CombatTestData.Monsters, stats: gripperStats, x: 1, y: 2);

        var encounter = Encounter.Start(new Battlefield(10, 5), [caster, gripper], random);

        return (encounter, caster, gripper);
    }

    /// <summary>
    /// A caster with the same "Ward" spell and one enemy ("gazer") armed with the
    /// Basilisk's own printed shape: a Bonus Action Constitution save, Restrained on
    /// the first failure, escalating to Petrified — and Incapacitated with it — on a
    /// second. The gazer's AC is high enough that Ward always misses it. The caster
    /// goes first.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Gazer) GazeStage(ScriptedRandomSource random)
    {
        var casterStats = CombatTestData.Stats(
            armorClass: 5,
            maximumHitPoints: 20,
            initiativeBonus: 10,
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
                Level: 3,
                Spells: [Ward()],
                SpellSlots: new Dictionary<int, int> { [1] = 1 },
                SpellcastingAbility: Ability.Intelligence,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, casterStats, new GridPosition(1, 5));

        var gazerStats = CombatTestData.Stats(
            armorClass: 30, // Ward's spell attack can never reach this.
            initiativeBonus: -10,
            attacks: []) with
        {
            Entries =
            [
                new MonsterEntry("Petrifying Gaze", MonsterEntrySection.BonusAction, "Petrifying Gaze.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: GazeSave()),
            ],
        };

        var gazer = CombatTestData.Combatant("gazer", sideId: CombatTestData.Monsters, stats: gazerStats, y: 5);

        var encounter = Encounter.Start(new Battlefield(10, 10), [caster, gazer], random);

        return (encounter, caster, gazer);
    }

    /// <summary>
    /// A melee spell attack, Concentration required, that always misses a high-AC
    /// target. Null range on purpose: a Ranged attack this close to the fight's only
    /// other combatant would earn Disadvantage of its own ("ranged within 5 feet of
    /// any able enemy") and cost an extra scripted roll unrelated to what these tests
    /// pin.
    /// </summary>
    private static SpellDefinition Ward() => new()
    {
        Id = "spell.ward",
        Name = "Ward",
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Wizard"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Concentration, up to 1 minute",
        RequiresConcentration = true,
        Mechanics = EntryMechanics.Attack,
        IsSpellAttack = true,
        SourcePage = 1,
        RangeText = "Touch",
        RangeFeet = null,
        Text = "A test ward.",
        Damage = [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Force, 3)],
    };

    /// <summary>
    /// The two-tier gaze's own save, DC set past anything a roll plus this project's
    /// hand-built abilities can meet: Restrained on the first failure, escalating to
    /// Petrified — the same shape as <c>PetrifiedTests.GazeSave</c>, reused here rather
    /// than shared because that DC (12) is tuned to a different victim's stat block.
    /// </summary>
    private static SaveEffect GazeSave() => new(
        Ability.Constitution,
        30,
        new EffectArea(AreaShape.Cone, 30),
        [],
        SaveSuccessOutcome.NoEffect,
        [
            new AppliedCondition(
                ConditionType.Restrained,
                Duration: ConditionDuration.UntilSavedOrEscalated,
                EscalatesTo: ConditionType.Petrified),
        ]);
}
