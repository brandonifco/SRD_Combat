using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Target-aware damage valuation (#224) reached only as far as <c>TryAttack</c>. #339
/// carries the same treatment — zeroed by an Immunity, halved by a Resistance, doubled
/// by a Vulnerability, all via <c>ResponseFactor</c> — into the sites <c>TryAttack</c>
/// never touches: a spell weighed against the swing it would replace, a limited-use
/// entry's own choice among itself, and the cost an Opportunity Attack is priced at
/// while planning a walk.
/// </summary>
public class TargetAwareValuationTests
{
    [Fact]
    public void ACasterWhoseOnlyWeaponIsImmuneCastsTheWorkingSpellInstead()
    {
        // The issue's own shape: a Slashing-only weapon against a Slashing-Immune
        // target. Before the fix, WeaponValue priced the Longsword at its raw 7.5 and
        // the caster never cleared IsWorthCasting's bar with a cantrip that actually
        // works — the #224 stall wearing a spellbook. Fixed, the Longsword prices at
        // zero against this target and the Fire cantrip (a real 5.5) wins outright.
        var firebolt = new SpellDefinition
        {
            Id = "spell.firebolt",
            Name = "firebolt",
            Level = 0,
            School = MagicSchool.Evocation,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Mechanics = EntryMechanics.SavingThrow,
            SourcePage = 1,
            RangeText = "120 feet",
            RangeFeet = 120,
            Text = "A test damaging cantrip.",
            Save = new SaveEffect(
                Ability.Dexterity,
                DifficultyClass: 13,
                Area: null,
                FailureDamage: [new AttackDamage(DiceExpression.Parse("3d3"), DamageType.Fire, 6)],
                SuccessOutcome: SaveSuccessOutcome.HalfDamage,
                AppliedConditions: []),
        };

        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            InitiativeBonus = 10,
            Attacks = [CombatTestData.MeleeAttack("Longsword", damage: "1d6 + 5", type: DamageType.Slashing)],
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3,
                Spells: [firebolt],
                SpellSlots: new Dictionary<int, int>(),
                SpellcastingAbility: Ability.Intelligence,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, stats, new GridPosition(0, 0));

        var jelly = CombatTestData.Combatant(
            "jelly",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                initiativeBonus: 0,
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Slashing] = DamageResponse.Immunity,
                }),
            x: 1);

        // Initiative, a failed save (below DC 13), and the failure's three d3s.
        var encounter = Encounter.Start(
            new Battlefield(6, 3),
            [caster, jelly],
            new ScriptedRandomSource(15, 1, 5, 2, 2, 2));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.SpellCast
                && step.Narration.Contains("firebolt", StringComparison.Ordinal));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.ActorId == caster.Id);
    }

    [Fact]
    public void AnImmuneCantripIsDeclinedInFavourOfTheWorkingSwing()
    {
        // The other half of the same gap: SpellValue itself ignoring immunity. The only
        // damaging spell is Fire, the target is Immune to Fire, and the mace works
        // fine. Before the fix, SpellValue priced the cantrip at its blind 10.5 average
        // — comfortably clearing IsWorthCasting's "just has to be better than the
        // swing" bar for a cantrip — and the caster burned its action on a cast that
        // could never deal a point of damage. Fixed, the cantrip prices at zero, is
        // filtered out before the comparison, and the mace swings instead.
        var firebolt = new SpellDefinition
        {
            Id = "spell.firebolt",
            Name = "firebolt",
            Level = 0,
            School = MagicSchool.Evocation,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Mechanics = EntryMechanics.SavingThrow,
            SourcePage = 1,
            RangeText = "120 feet",
            RangeFeet = 120,
            Text = "A test damaging cantrip.",
            Save = new SaveEffect(
                Ability.Dexterity,
                DifficultyClass: 13,
                Area: null,
                FailureDamage: [new AttackDamage(DiceExpression.Parse("3d6"), DamageType.Fire, 11)],
                SuccessOutcome: SaveSuccessOutcome.HalfDamage,
                AppliedConditions: []),
        };

        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            InitiativeBonus = 10,
            Attacks = [CombatTestData.MeleeAttack("Mace", damage: "1d6 + 2", type: DamageType.Bludgeoning)],
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3,
                Spells: [firebolt],
                SpellSlots: new Dictionary<int, int>(),
                SpellcastingAbility: Ability.Intelligence,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, stats, new GridPosition(0, 0));

        var target = CombatTestData.Combatant(
            "wraith",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                initiativeBonus: 0,
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Fire] = DamageResponse.Immunity,
                }),
            x: 1);

        // Initiative, then the mace's attack roll and its damage die.
        var encounter = Encounter.Start(
            new Battlefield(6, 3),
            [caster, target],
            new ScriptedRandomSource(15, 1, 10, 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        var attack = Assert.Single(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.ActorId == caster.Id);
        Assert.Contains("Mace", attack.Narration);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Kind == CombatStepKind.Spell);
    }

    [Fact]
    public void TryUseLimitedEntryPicksTheNonImmuneEntryOverTheHarderRawOne()
    {
        // Two limited-use saving-throw entries and no ordinary attack at all, so the
        // Attack action never intervenes: Fire Breath prints the bigger average (10.5)
        // but the target is Immune to Fire; Ice Bite prints less (5) but actually
        // lands. Before the fix the raw-average ordering always reached for Fire
        // Breath and never dealt a point.
        var fireBreath = new MonsterEntry(
            "Fire Breath",
            MonsterEntrySection.Action,
            "Breathes fire.",
            Mechanics: EntryMechanics.SavingThrow,
            Save: new SaveEffect(
                Ability.Dexterity,
                12,
                null,
                [new AttackDamage(DiceExpression.Parse("3d6"), DamageType.Fire, 11)],
                SaveSuccessOutcome.HalfDamage,
                []),
            Usage: new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 1));

        var iceBite = new MonsterEntry(
            "Ice Bite",
            MonsterEntrySection.Action,
            "Bites with cold.",
            Mechanics: EntryMechanics.SavingThrow,
            Save: new SaveEffect(
                Ability.Dexterity,
                12,
                null,
                [new AttackDamage(DiceExpression.Parse("2d4"), DamageType.Cold, 5)],
                SaveSuccessOutcome.HalfDamage,
                []),
            Usage: new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 1));

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries = [fireBreath, iceBite],
        };

        var actor = CombatTestData.Combatant("breather", sideId: CombatTestData.Monsters, stats: stats);

        var target = CombatTestData.Combatant(
            "victim",
            stats: CombatTestData.Stats(
                maximumHitPoints: 40,
                initiativeBonus: -10,
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Fire] = DamageResponse.Immunity,
                }),
            x: 2);

        // Initiative, a failed save against Ice Bite (below DC 12), and its two d4s.
        var encounter = Encounter.Start(
            new Battlefield(6, 3),
            [actor, target],
            new ScriptedRandomSource(20, 1, 5, 2, 2));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Ice Bite", StringComparison.Ordinal));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Fire Breath", StringComparison.Ordinal));
    }

    [Fact]
    public void TryUseLimitedEntryHalvesAResistedEntryRatherThanJustZeroingIt()
    {
        // Resistance is not a second Immunity: it halves rather than zeroes, so the
        // ordering has to be a genuine comparison. Fire Breath's raw 7.5 would still
        // beat Ice Bite's 5 blind, and would still beat it at zero — the only figure
        // that actually flips the ordering is the halved 3.75, which is what proves
        // this site multiplies by 0.5 rather than just special-casing Immunity.
        var fireBreath = new MonsterEntry(
            "Fire Breath",
            MonsterEntrySection.Action,
            "Breathes fire.",
            Mechanics: EntryMechanics.SavingThrow,
            Save: new SaveEffect(
                Ability.Dexterity,
                12,
                null,
                [new AttackDamage(DiceExpression.Parse("1d10 + 2"), DamageType.Fire, 8)],
                SaveSuccessOutcome.HalfDamage,
                []),
            Usage: new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 1));

        var iceBite = new MonsterEntry(
            "Ice Bite",
            MonsterEntrySection.Action,
            "Bites with cold.",
            Mechanics: EntryMechanics.SavingThrow,
            Save: new SaveEffect(
                Ability.Dexterity,
                12,
                null,
                [new AttackDamage(DiceExpression.Parse("2d4"), DamageType.Cold, 5)],
                SaveSuccessOutcome.HalfDamage,
                []),
            Usage: new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 1));

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries = [fireBreath, iceBite],
        };

        var actor = CombatTestData.Combatant("breather", sideId: CombatTestData.Monsters, stats: stats);

        var target = CombatTestData.Combatant(
            "victim",
            stats: CombatTestData.Stats(
                maximumHitPoints: 40,
                initiativeBonus: -10,
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Fire] = DamageResponse.Resistance,
                }),
            x: 2);

        // Initiative, a failed save against Ice Bite (below DC 12), and its two d4s.
        var encounter = Encounter.Start(
            new Battlefield(6, 3),
            [actor, target],
            new ScriptedRandomSource(20, 1, 5, 2, 2));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Ice Bite", StringComparison.Ordinal));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Fire Breath", StringComparison.Ordinal));
    }

    [Fact]
    public void AVulnerableTargetTipsAnOtherwiseWeakerSpellIntoWorthCasting()
    {
        // The other multiplier: Vulnerability doubles. The cantrip's raw 5 does not
        // clear the mace's 6.5 blind, so a blind valuation swings the mace regardless
        // of any target-awareness that only ever zeroes — doubling to 10 against a
        // Vulnerable target is what actually flips the choice to the cast, proving
        // this site multiplies by 2 rather than only ever zeroing an Immunity.
        var frostbolt = new SpellDefinition
        {
            Id = "spell.frostbolt",
            Name = "frostbolt",
            Level = 0,
            School = MagicSchool.Evocation,
            Classes = ["Wizard"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Mechanics = EntryMechanics.SavingThrow,
            SourcePage = 1,
            RangeText = "120 feet",
            RangeFeet = 120,
            Text = "A test damaging cantrip.",
            Save = new SaveEffect(
                Ability.Dexterity,
                DifficultyClass: 13,
                Area: null,
                FailureDamage: [new AttackDamage(DiceExpression.Parse("2d4"), DamageType.Cold, 5)],
                SuccessOutcome: SaveSuccessOutcome.HalfDamage,
                AppliedConditions: []),
        };

        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            InitiativeBonus = 10,
            Attacks = [CombatTestData.MeleeAttack("Mace", damage: "1d10 + 1", type: DamageType.Bludgeoning)],
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3,
                Spells: [frostbolt],
                SpellSlots: new Dictionary<int, int>(),
                SpellcastingAbility: Ability.Intelligence,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, stats, new GridPosition(0, 0));

        var target = CombatTestData.Combatant(
            "troll",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                initiativeBonus: 0,
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Cold] = DamageResponse.Vulnerability,
                }),
            x: 1);

        // Initiative, a failed save (below DC 13), and the failure's two d4s.
        var encounter = Encounter.Start(
            new Battlefield(6, 3),
            [caster, target],
            new ScriptedRandomSource(15, 1, 5, 2, 2));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.SpellCast
                && step.Narration.Contains("frostbolt", StringComparison.Ordinal));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.ActorId == caster.Id);
    }

    [Fact]
    public void TryUseBonusEntryPicksTheNonImmuneEntryOverTheHarderRawOne()
    {
        // The identical shape as the Action-slot test, one turn earlier in the flow:
        // TryUseBonusEntry carries the same duplicated switch and needed the same fix.
        var fireGaze = new MonsterEntry(
            "Fire Gaze",
            MonsterEntrySection.BonusAction,
            "Glares with fire.",
            Mechanics: EntryMechanics.SavingThrow,
            Save: new SaveEffect(
                Ability.Wisdom,
                12,
                null,
                [new AttackDamage(DiceExpression.Parse("3d6"), DamageType.Fire, 11)],
                SaveSuccessOutcome.HalfDamage,
                []),
            Usage: new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 1));

        var frostGaze = new MonsterEntry(
            "Frost Gaze",
            MonsterEntrySection.BonusAction,
            "Glares with frost.",
            Mechanics: EntryMechanics.SavingThrow,
            Save: new SaveEffect(
                Ability.Wisdom,
                12,
                null,
                [new AttackDamage(DiceExpression.Parse("2d4"), DamageType.Cold, 5)],
                SaveSuccessOutcome.HalfDamage,
                []),
            Usage: new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 1));

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries = [fireGaze, frostGaze],
        };

        var actor = CombatTestData.Combatant("glarer", sideId: CombatTestData.Monsters, stats: stats);

        var target = CombatTestData.Combatant(
            "victim",
            stats: CombatTestData.Stats(
                maximumHitPoints: 40,
                initiativeBonus: -10,
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    [DamageType.Fire] = DamageResponse.Immunity,
                }),
            x: 2);

        // Initiative, a failed save against Frost Gaze (below DC 12), and its two d4s.
        var encounter = Encounter.Start(
            new Battlefield(6, 3),
            [actor, target],
            new ScriptedRandomSource(20, 1, 5, 2, 2));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Frost Gaze", StringComparison.Ordinal));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Fire Gaze", StringComparison.Ordinal));
    }

    [Fact]
    public void AWalkThatProvokesButCannotBeHurtIsTakenAnyway()
    {
        // The corridor from ProvokedMovementTests, with one change: the archer is
        // Immune to the melee enemy's damage type. Before the fix, ProvokedDamageAlong
        // priced the sidestep's Opportunity Attack at the enemy's raw average
        // regardless — the same cost as if the archer could actually be hurt — so
        // ImproveFiringPosition's zero-provocation veto still refused the free
        // sidestep and the archer kept shooting through the ally's Half Cover. Fixed,
        // the same swing prices at zero against an Immune target and the sidestep is
        // taken, exactly as it already is when the enemy is removed outright.
        var archer = CombatTestData.Combatant(
            "archer",
            stats: CombatTestData.Stats(
                initiativeBonus: 10,
                attacks: [CombatTestData.RangedAttack(bonus: 4)],
                damageResponses: new Dictionary<DamageType, DamageResponse>
                {
                    // CombatTestData.MeleeAttack defaults to Slashing.
                    [DamageType.Slashing] = DamageResponse.Immunity,
                },
                intelligence: 3),
            x: 1,
            y: 0);

        var ally = CombatTestData.Combatant(
            "ally",
            stats: CombatTestData.Stats(initiativeBonus: -5, attacks: []),
            x: 2,
            y: 0);

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(maximumHitPoints: 5, initiativeBonus: -10, attacks: []),
            x: 5,
            y: 0);

        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                maximumHitPoints: 40,
                initiativeBonus: -8,
                attacks: [CombatTestData.MeleeAttack(bonus: 4)]),
            x: 0,
            y: 0);

        // Initiative for all four, then the enemy's Opportunity Attack (a hit that
        // deals no damage) and the archer's own clean shot at the target.
        var encounter = Encounter.Start(
            new Battlefield(7, 1),
            [archer, ally, target, enemy],
            new ScriptedRandomSource(20, 1, 1, 1, 14, 4, 14, 4));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(new GridPosition(3, 0), archer.Position);

        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Damage
                && step.Narration.Contains("archer takes 0 Slashing damage (Immune)", StringComparison.Ordinal));

        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.DoesNotContain("Cover", swing.Narration);
    }
}
