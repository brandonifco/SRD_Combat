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

    [Fact]
    public void SweepConcentrationConditionsLiftsAHeldVictimWhenTheCasterIsParalyzed()
    {
        // #336: every test above casts Ward, which imposes no ongoing condition of its
        // own, so SweepConcentrationConditions always ran over nothing — the sweep
        // itself was unexercised. Here the caster concentrates on a Hold-shaped spell
        // that Paralyzes victimB for the duration, tied to the caster's own
        // Concentration (ConditionDuration.ConcentrationUpToOneMinuteWithRepeatSave,
        // the same duration Hold Person prints). The caster is then Paralyzed in turn
        // by a rider with no damage attached, breaking its own Concentration — and in
        // that same UseEntry call, sweeping victimB's Paralyzed away too.
        //
        // Initiative ×3 in combatant order (caster, victimB, attacker) — the attacker
        // outranks victimB so its turn comes second, before victimB's own turn could
        // ever roll the held condition's repeat save and consume a stray die. Then
        // Hold's save roll for victimB (DC 30, no d20 result plus Wisdom +0 reaches
        // it) and Grip's save roll for the caster (DC 30, no roll plus Constitution +2
        // reaches it either). Exactly five rolls: a stray save on top would throw.
        var (encounter, caster, victimB, attacker) = HoldStage(new ScriptedRandomSource(15, 1, 1, 1, 10));

        Assert.Null(encounter.CastSpell("spell.hold", victimB));
        Assert.Equal("Hold", caster.Features.ConcentratingOn);
        Assert.True(victimB.HasCondition(ConditionType.Paralyzed));

        encounter.EndTurn(); // The caster's turn ends; the attacker's begins.

        Assert.Null(encounter.UseEntry("Grip", caster));

        Assert.True(caster.HasCondition(ConditionType.Paralyzed));
        Assert.True(caster.HasCondition(ConditionType.Incapacitated));
        Assert.Null(caster.Features.ConcentratingOn);

        // The condition Hold tied to the caster's Concentration lifts off victimB in
        // the very same step that Incapacitates the caster — the sweep actually
        // running over something, unlike the Ward-based tests above.
        Assert.False(victimB.HasCondition(ConditionType.Paralyzed));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("caster loses Concentration on Hold", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains(
                "victimB is no longer Paralyzed — the spell holding it has ended",
                StringComparison.Ordinal));
    }

    [Fact]
    public void TurningAConcentratingUndeadBreaksItsConcentrationWithNoAssertionFiring()
    {
        // #335 review found an eighth narrate-then-break window this file's own tests
        // never exercised: Turn Undead narrates its own standalone Incapacitated
        // condition (TurnUndeadConditions' second entry) before the trailing
        // BreakConcentrationOnIncapacitated call, with no SuspendConcentrationInvariant
        // wrapper — so turning a concentrating Undead would have tripped the #335
        // assertion as a false positive. An Undead spellcaster is an exotic shape, but
        // exactly the one "a future condition-landing path" describes: this pins both
        // that the assertion added for #335 does not misfire on Turn Undead's own
        // narration, and that Turn Undead genuinely breaks the Undead's Concentration.
        //
        // Initiative ×2 (the undead first, the cleric second — set by initiative bonus
        // rather than list order), Ward's spell attack roll for the undead's own turn
        // (the cleric's AC is set past anything the roll can reach, so it always
        // misses and never spends a damage die), then the undead's Wisdom saving
        // throw against Turn Undead (DC 10, this file's own fallback arithmetic; a
        // roll of 1 plus a +0 bonus fails). Exactly four rolls.
        var (encounter, cleric, undead) = ConcentratingUndeadStage(new ScriptedRandomSource(1, 1, 10, 1));

        Assert.Null(encounter.CastSpell("spell.ward", cleric));
        Assert.Equal("Ward", undead.Features.ConcentratingOn);

        encounter.EndTurn(); // The undead's turn ends; the cleric's begins.

        Assert.Null(encounter.TurnUndead([undead]));

        Assert.True(undead.HasCondition(ConditionType.Incapacitated));
        Assert.Null(undead.Features.ConcentratingOn);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("undead loses Concentration on Ward", StringComparison.Ordinal));
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

    /// <summary>
    /// A caster with a hand-built Hold Person-shaped spell ("Hold") that Paralyzes a
    /// single target for the duration of the caster's own Concentration, one victim
    /// ("victimB") to hold, and one enemy ("attacker") armed with the same
    /// no-damage, unbeatable-DC "Grip" entry the other stage uses — here aimed back at
    /// the caster instead of the other way around. Initiative order is caster,
    /// attacker, victimB: the attacker's turn comes before victimB ever gets one, so
    /// the sweep below runs before victimB's own held condition could roll its repeat
    /// save and consume a die the tests do not script. Dice beyond initiative are
    /// whatever each test's own turns consume.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant VictimB, Combatant Attacker) HoldStage(
        ScriptedRandomSource random)
    {
        var casterStats = CombatTestData.Stats(
            armorClass: 5,
            maximumHitPoints: 20,
            initiativeBonus: 20) with
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
                Spells: [Hold()],
                SpellSlots: new Dictionary<int, int> { [2] = 1 },
                SpellcastingAbility: Ability.Intelligence,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, casterStats, new GridPosition(0, 5));

        var victimB = CombatTestData.Combatant(
            "victimB",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -20),
            x: 5,
            y: 5);

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

        var attackerStats = CombatTestData.Stats(initiativeBonus: 0) with
        {
            Entries =
            [
                new MonsterEntry("Grip", MonsterEntrySection.Action, "Grip.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: grip),
            ],
        };

        var attacker = CombatTestData.Combatant(
            "attacker", sideId: CombatTestData.Monsters, stats: attackerStats, x: 1, y: 5);

        var encounter = Encounter.Start(new Battlefield(10, 10), [caster, victimB, attacker], random);

        return (encounter, caster, victimB, attacker);
    }

    /// <summary>
    /// Hold Person's own printed clock (SRD 5.2.1): a Wisdom save or Paralyzed "for the
    /// duration" of Concentration, up to 1 minute, with a repeat save at the end of
    /// each of the target's own turns. The DC is hardcoded past anything a roll can
    /// meet, the same convention <see cref="GazeSave"/> and <c>Grip</c> use, so victimB's
    /// failure never depends on the scripted roll's value.
    /// </summary>
    private static SpellDefinition Hold() => new()
    {
        Id = "spell.hold",
        Name = "Hold",
        Level = 2,
        School = MagicSchool.Enchantment,
        Classes = ["Wizard"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Concentration, up to 1 minute",
        RequiresConcentration = true,
        Mechanics = EntryMechanics.SavingThrow,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test hold.",
        Save = new SaveEffect(
            Ability.Wisdom,
            30, // No d20 result plus a +0 save bonus reaches this.
            Area: null,
            FailureDamage: [],
            SuccessOutcome: SaveSuccessOutcome.NoEffect,
            AppliedConditions:
            [
                new AppliedCondition(
                    ConditionType.Paralyzed,
                    Duration: ConditionDuration.ConcentrationUpToOneMinuteWithRepeatSave),
            ]),
    };

    /// <summary>
    /// A Cleric with Channel Divinity and one enemy ("undead") that is both a valid
    /// Turn Undead target (<see cref="CreatureType.Undead"/>) and a spellcaster in its
    /// own right, armed with the same "Ward" spell the other stage uses. The Cleric's
    /// Wisdom is left at this file's default (10, no proficiency), so Turn Undead's DC
    /// falls back to <c>SpellcastingRules.SaveDifficultyClass</c>'s 8 + 2 + 0 = 10, and
    /// the undead's own Wisdom save bonus is the same default 0 — matching
    /// <c>TurnUndeadTests</c>' own documented fallback arithmetic. The Cleric's AC is
    /// set past anything Ward's spell attack can reach, so casting it at the Cleric
    /// never spends a damage die. The undead goes first.
    /// </summary>
    private static (Encounter Encounter, Combatant Cleric, Combatant Undead) ConcentratingUndeadStage(
        ScriptedRandomSource random)
    {
        var clericStats = CombatTestData.Stats(armorClass: 30, initiativeBonus: -10) with
        {
            Character = new CombatantFeatures(
                [ClassFeature.ChannelDivinity],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3,
                ChannelDivinityUses: 2),
        };

        var cleric = new Combatant("cleric", "cleric", CombatTestData.Heroes, clericStats, new GridPosition(0, 5));

        var undeadStats = CombatTestData.Stats(initiativeBonus: 20) with
        {
            Type = CreatureType.Undead,
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

        var undead = new Combatant("undead", "undead", CombatTestData.Monsters, undeadStats, new GridPosition(1, 5));

        var encounter = Encounter.Start(new Battlefield(10, 10), [cleric, undead], random);

        return (encounter, cleric, undead);
    }
}
