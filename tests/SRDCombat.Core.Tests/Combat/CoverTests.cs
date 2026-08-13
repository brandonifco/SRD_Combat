using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Cover, from the Combat chapter's printed table: Half is +2 to AC and Dexterity saving
/// throws, Three-Quarters +5, Total "can't be targeted directly", and only the most
/// protective degree applies.
/// </summary>
/// <remarks>
/// The grid geometry — the centre-to-centre segment, interiors rather than corners — is
/// <c>CoverRules</c>' stated interpretation, and the geometry tests pin that reading so
/// a change to it is a decision rather than an accident.
/// </remarks>
public class CoverTests
{
    // ── The geometry ────────────────────────────────────────────────────────────

    [Fact]
    public void OpenGround_IsNoCover() =>
        Assert.Equal(
            CoverDegree.None,
            CoverRules.Between(new Battlefield(9, 5), new GridPosition(0, 2), new GridPosition(4, 2)));

    [Fact]
    public void AWallOnTheLine_IsTotalCover() =>
        Assert.Equal(
            CoverDegree.Total,
            CoverRules.Between(
                new Battlefield(9, 5, blocked: [new GridPosition(2, 2)]),
                new GridPosition(0, 2),
                new GridPosition(4, 2)));

    [Fact]
    public void ALowObstacleOnTheLine_IsHalfCover() =>
        Assert.Equal(
            CoverDegree.Half,
            CoverRules.Between(
                new Battlefield(9, 5, lowObstacles: [new GridPosition(2, 2)]),
                new GridPosition(0, 2),
                new GridPosition(4, 2)));

    [Fact]
    public void TwoLowObstaclesOnTheLine_AreThreeQuarters() =>
        Assert.Equal(
            CoverDegree.ThreeQuarters,
            CoverRules.Between(
                new Battlefield(9, 5, lowObstacles: [new GridPosition(1, 2), new GridPosition(2, 2)]),
                new GridPosition(0, 2),
                new GridPosition(4, 2)));

    [Fact]
    public void OnlyTheMostProtectiveDegreeApplies()
    {
        // A wall and a low obstacle on the same line: Total, not some sum — "the degrees
        // aren't added together".
        var field = new Battlefield(
            9,
            5,
            blocked: [new GridPosition(1, 2)],
            lowObstacles: [new GridPosition(2, 2)]);

        Assert.Equal(
            CoverDegree.Total,
            CoverRules.Between(field, new GridPosition(0, 2), new GridPosition(4, 2)));
    }

    [Fact]
    public void ACornerTouch_IsNotCover()
    {
        // The diagonal from (0,0) to (2,2) touches (1,0)'s corner and crosses (1,1)'s
        // interior: a seam is not a wall, a square in the way is.
        var toucher = new Battlefield(4, 4, blocked: [new GridPosition(1, 0)]);
        var blocker = new Battlefield(4, 4, blocked: [new GridPosition(1, 1)]);

        Assert.Equal(
            CoverDegree.None,
            CoverRules.Between(toucher, new GridPosition(0, 0), new GridPosition(2, 2)));
        Assert.Equal(
            CoverDegree.Total,
            CoverRules.Between(blocker, new GridPosition(0, 0), new GridPosition(2, 2)));
    }

    [Fact]
    public void AdjacentSquares_HaveNothingBetween()
    {
        var field = new Battlefield(4, 4, blocked: [new GridPosition(2, 2)]);

        // Diagonal neighbours whose shared corner touches the wall: the segment brushes
        // the wall's corner and crosses nothing, so melee across it stays clean.
        Assert.Equal(
            CoverDegree.None,
            CoverRules.Between(field, new GridPosition(2, 1), new GridPosition(3, 2)));
        Assert.Equal(
            CoverDegree.None,
            CoverRules.Between(field, new GridPosition(0, 0), new GridPosition(1, 1)));
    }

    [Fact]
    public void CoverBehindTheTarget_DoesNotCount() =>
        // "Only when an attack or other effect originates on the opposite side."
        Assert.Equal(
            CoverDegree.None,
            CoverRules.Between(
                new Battlefield(9, 5, blocked: [new GridPosition(3, 2)]),
                new GridPosition(0, 2),
                new GridPosition(2, 2)));

    // ── The attack roll ─────────────────────────────────────────────────────────

    [Fact]
    public void HalfCoverRaisesTheNumberToBeat()
    {
        var archer = CombatTestData.Combatant("archer");
        var bow = CombatTestData.RangedAttack(bonus: 4);
        var target = CombatTestData.Combatant("orc", sideId: CombatTestData.Monsters, x: 4);

        // A natural 10 + 4 = 14 beats the bare AC 13 and loses to 13 + 2.
        var roll = AttackRules.Resolve(
            new ScriptedRandomSource(10),
            archer,
            bow,
            target,
            cover: CoverDegree.Half);

        Assert.False(roll.Hit);
        Assert.Equal(15, roll.TargetArmorClass);
        Assert.Equal(CoverDegree.Half, roll.Cover);
    }

    [Fact]
    public void ANatural20_StillHitsThroughThreeQuarters()
    {
        var archer = CombatTestData.Combatant("archer");
        var target = CombatTestData.Combatant("orc", sideId: CombatTestData.Monsters, x: 4);

        var roll = AttackRules.Resolve(
            new ScriptedRandomSource(20, 4),
            archer,
            CombatTestData.RangedAttack(bonus: 0),
            target,
            cover: CoverDegree.ThreeQuarters);

        Assert.True(roll.Hit);
        Assert.True(roll.Critical);
    }

    // ── The encounter: refusals, narration, saves ───────────────────────────────

    [Fact]
    public void TotalCover_RefusesTheAttack_BeforeAnythingIsSpent()
    {
        var (encounter, archer, brute) = Shootout(new Battlefield(9, 3, blocked: [new GridPosition(2, 1)]));

        var refusal = encounter.Attack("Bow", brute);

        Assert.Equal("attack.total_cover", refusal?.Code);
        Assert.True(archer.Turn.HasAction);
    }

    [Fact]
    public void HalfCover_RaisesTheArmorClass_AndTheNarrationSaysWhy()
    {
        var (encounter, _, brute) = Shootout(
            new Battlefield(9, 3, lowObstacles: [new GridPosition(2, 1)]),
            attackRolls: [10]);

        Assert.Null(encounter.Attack("Bow", brute));

        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.Contains("vs AC 15 (Half Cover) — miss", swing.Narration);
    }

    [Fact]
    public void ADexteritySave_GainsTheCoverBonus()
    {
        // Save bonus +2 and Half Cover +2 turn a rolled 10 into 14 against DC 14 —
        // without the cover it fails.
        var (encounter, target) = SpellFight(
            DexSaveSpell(),
            new Battlefield(9, 3, lowObstacles: [new GridPosition(2, 1)]),
            scripted: [20, 1, 10, 3, 3]);

        Assert.Null(encounter.CastSpell("spell.test-flame", target));

        var save = encounter.Log.Last(step => step.Narration.Contains("saving throw"));
        Assert.Contains("(Half Cover)", save.Narration);
        Assert.Contains("success", save.Narration);
    }

    [Fact]
    public void AConstitutionSave_GainsNothingFromCover()
    {
        var (encounter, target) = SpellFight(
            DexSaveSpell() with
            {
                Save = DexSaveSpell().Save! with { Ability = Ability.Constitution },
            },
            new Battlefield(9, 3, lowObstacles: [new GridPosition(2, 1)]),
            scripted: [20, 1, 10, 3, 3]);

        Assert.Null(encounter.CastSpell("spell.test-flame", target));

        var save = encounter.Log.Last(step => step.Narration.Contains("saving throw"));
        Assert.DoesNotContain("Cover", save.Narration);
        Assert.Contains("failure", save.Narration);
    }

    [Fact]
    public void ASaveThatIgnoresCover_IgnoresIt()
    {
        // Sacred Flame's shape: "The target gains no benefit from Half Cover or
        // Three-Quarters Cover for this save", carried as Save.CoverIgnored.
        var (encounter, target) = SpellFight(
            DexSaveSpell() with { Save = DexSaveSpell().Save! with { CoverIgnored = true } },
            new Battlefield(9, 3, lowObstacles: [new GridPosition(2, 1)]),
            scripted: [20, 1, 10, 3, 3]);

        Assert.Null(encounter.CastSpell("spell.test-flame", target));

        var save = encounter.Log.Last(step => step.Narration.Contains("saving throw"));
        Assert.DoesNotContain("Cover", save.Narration);
        Assert.Contains("failure", save.Narration);
    }

    [Fact]
    public void TheWand_IgnoresHalfCoverOnSpellAttacks()
    {
        var (encounter, target) = SpellFight(
            AttackSpell(),
            new Battlefield(9, 3, lowObstacles: [new GridPosition(2, 1)]),
            scripted: [20, 1, 10, 4],
            ignoresHalfCover: true);

        Assert.Null(encounter.CastSpell("spell.test-ray", target));

        // 10 + 6 = 16 against the bare AC 13: the +2 the obstacle would add is ignored,
        // and the narration claims no cover.
        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.Contains("vs AC 13 — hit", swing.Narration);
        Assert.DoesNotContain("Cover", swing.Narration);
    }

    [Fact]
    public void WithoutTheWand_TheSameShotSuffersThePlusTwo()
    {
        var (encounter, target) = SpellFight(
            AttackSpell(),
            new Battlefield(9, 3, lowObstacles: [new GridPosition(2, 1)]),
            scripted: [20, 1, 10, 4]);

        Assert.Null(encounter.CastSpell("spell.test-ray", target));

        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.Contains("vs AC 15 (Half Cover) — hit", swing.Narration);
    }

    [Fact]
    public void ATargetedSpell_IsRefusedThroughTotalCover()
    {
        var (encounter, target) = SpellFight(
            AttackSpell(),
            new Battlefield(9, 3, blocked: [new GridPosition(2, 1)]),
            scripted: [20, 1]);

        var refusal = encounter.CastSpell("spell.test-ray", target);

        Assert.Equal("spell.total_cover", refusal?.Code);
    }

    [Fact]
    public void ThePolicySidestepsAWall_AndPrefersTheCleanShot()
    {
        // The archer's straight shot crosses the wall at (2,1) — Total Cover, refused —
        // so its turn becomes: move to a square that can deliver the attack, then
        // shoot. Row 0 fires past the low obstacle at (2,0), paying +2 on its own
        // shot; row 2 is a clean line. The clean shot outranks the shelter the
        // obstacle would give (#108's ordering), so the policy takes row 2 and rolls
        // against the bare AC. Speed 10 keeps the adjacent squares out of the
        // question.
        var archer = CombatTestData.Combatant(
            "archer",
            stats: CombatTestData.Stats(
                initiativeBonus: 10,
                speedFeet: 10,
                attacks: [CombatTestData.RangedAttack(bonus: 4)]),
            y: 1);

        var brute = CombatTestData.Combatant(
            "brute",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, attacks: []),
            x: 4,
            y: 1);

        var encounter = Encounter.Start(
            new Battlefield(9, 3, blocked: [new GridPosition(2, 1)], lowObstacles: [new GridPosition(2, 0)]),
            [archer, brute],
            new ScriptedRandomSource(20, 1, 14, 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(new GridPosition(2, 2), archer.Position);

        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.Contains("vs AC 13 — hit", swing.Narration);
        Assert.DoesNotContain("Cover", swing.Narration);
    }

    // ── Creatures as cover (#108) ───────────────────────────────────────────────

    [Fact]
    public void ALivingCreatureOnTheLine_IsHalfCover()
    {
        var bystander = CombatTestData.Combatant("bystander", x: 2, y: 1);

        Assert.Equal(
            CoverDegree.Half,
            CoverRules.Between(
                new Battlefield(9, 3),
                new GridPosition(0, 1),
                new GridPosition(4, 1),
                [bystander]));
    }

    [Fact]
    public void ADeadCreature_GrantsNothing()
    {
        // A fallen body lies flat and covers less than half of a standing target — the
        // same line MovementRules draws when the dead stop occupying their square.
        var corpse = CombatTestData.Combatant("corpse", x: 2, y: 1);
        DamageRules.Apply(corpse, 20, DamageType.Slashing);
        Assert.True(corpse.IsDead);

        Assert.Equal(
            CoverDegree.None,
            CoverRules.Between(
                new Battlefield(9, 3),
                new GridPosition(0, 1),
                new GridPosition(4, 1),
                [corpse]));
    }

    [Fact]
    public void ACrowdIsStillHalfCover()
    {
        // The printed table reserves Three-Quarters and Total for objects; however many
        // creatures the line crosses, crowds are not walls.
        var first = CombatTestData.Combatant("first", x: 1, y: 1);
        var second = CombatTestData.Combatant("second", x: 2, y: 1);

        Assert.Equal(
            CoverDegree.Half,
            CoverRules.Between(
                new Battlefield(9, 3),
                new GridPosition(0, 1),
                new GridPosition(4, 1),
                [first, second]));
    }

    [Fact]
    public void CreaturesNeverEscalateObstacles()
    {
        // A creature beside one low obstacle is two sources of Half, and Half is what
        // applies; two low obstacles are Three-Quarters on their own, and the creature
        // does not raise that either.
        var bystander = CombatTestData.Combatant("bystander", x: 3, y: 1);
        var oneObstacle = new Battlefield(9, 3, lowObstacles: [new GridPosition(2, 1)]);
        var twoObstacles = new Battlefield(
            9, 3, lowObstacles: [new GridPosition(1, 1), new GridPosition(2, 1)]);

        Assert.Equal(
            CoverDegree.Half,
            CoverRules.Between(oneObstacle, new GridPosition(0, 1), new GridPosition(4, 1), [bystander]));
        Assert.Equal(
            CoverDegree.ThreeQuarters,
            CoverRules.Between(twoObstacles, new GridPosition(0, 1), new GridPosition(4, 1), [bystander]));
    }

    [Fact]
    public void ShootingPastABystander_RaisesTheTargetsArmorClass()
    {
        var archer = CombatTestData.Combatant(
            "archer",
            stats: CombatTestData.Stats(
                initiativeBonus: 10,
                attacks: [CombatTestData.RangedAttack(bonus: 4)]),
            y: 1);

        var bystander = CombatTestData.Combatant(
            "bystander",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(attacks: []),
            x: 2,
            y: 1);

        var brute = CombatTestData.Combatant(
            "brute",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, attacks: []),
            x: 4,
            y: 1);

        var encounter = Encounter.Start(
            new Battlefield(9, 3),
            [archer, bystander, brute],
            new ScriptedRandomSource(20, 10, 1, 14, 3));

        Assert.Null(encounter.Attack("Bow", brute));

        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.Contains("vs AC 15 (Half Cover)", swing.Narration);
    }

    [Fact]
    public void TheArcherStepsAroundItsAlly_InsteadOfShootingThroughIt()
    {
        // The behaviour #108 was deferred over: a shot through an ally is legal at +2,
        // so the old turn shape took it every time and never reached the movement that
        // would have cleared the line. ImproveFiringPosition steps aside first, to a
        // square with a strictly cheaper shot that is not beside an enemy, and the
        // attack rolls against the bare AC.
        var archer = CombatTestData.Combatant(
            "archer",
            stats: CombatTestData.Stats(
                initiativeBonus: 10,
                attacks: [CombatTestData.RangedAttack(bonus: 4)]),
            y: 1);

        var ally = CombatTestData.Combatant(
            "ally",
            stats: CombatTestData.Stats(initiativeBonus: -5, attacks: []),
            x: 1,
            y: 1);

        var brute = CombatTestData.Combatant(
            "brute",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, attacks: []),
            x: 4,
            y: 1);

        var encounter = Encounter.Start(
            new Battlefield(9, 3),
            [archer, ally, brute],
            new ScriptedRandomSource(20, 10, 1, 14, 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        // Behaviour, not a square: the archer moved somewhere with a clean line, out of
        // close combat, and rolled against the bare AC.
        Assert.NotEqual(new GridPosition(0, 1), archer.Position);
        Assert.Equal(
            CoverDegree.None,
            CoverRules.Between(encounter.Battlefield, archer.Position, brute.Position, [ally, brute]));
        Assert.True(archer.Position.DistanceFeetTo(brute.Position) > Battlefield.FeetPerSquare);

        var swing = encounter.Log.Last(step => step.Narration.Contains("attacks"));
        Assert.Contains("vs AC 13 — hit", swing.Narration);
        Assert.DoesNotContain("Cover", swing.Narration);
    }

    [Fact]
    public void AnOpportunityAttackDoesNotSwingThroughTotalCover()
    {
        // "Melee reach means nothing in between" is false for a reach weapon: a
        // Halberd's Opportunity Attack spans a square, and that square can be a wall.
        // The mover slips away unswung-at, the way a charmer does.
        var sentinel = CombatTestData.Combatant(
            "sentinel",
            stats: CombatTestData.Stats(
                initiativeBonus: -10,
                attacks: [CombatTestData.MeleeAttack("Halberd", reachFeet: 10)]),
            x: 0,
            y: 1);

        var runner = CombatTestData.Combatant(
            "runner",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: 10),
            x: 2,
            y: 1);

        var encounter = Encounter.Start(
            new Battlefield(9, 3, blocked: [new GridPosition(1, 1)]),
            [sentinel, runner],
            new ScriptedRandomSource(20, 1));

        Assert.Null(encounter.Move(new GridPosition(4, 1)));

        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
        Assert.True(sentinel.Turn.HasReaction);
    }

    // ── Areas of effect ─────────────────────────────────────────────────────────

    [Fact]
    public void AnAreaExcludesSquaresBehindTotalCover()
    {
        // A Sphere erupting at (2,1) with a wall at (3,1): the squares whose
        // centre-to-centre line from the point of origin crosses the wall are outside
        // the area — the glossary's Areas of Effect rule.
        var field = new Battlefield(7, 3, blocked: [new GridPosition(3, 1)]);
        var sphere = new EffectArea(AreaShape.Sphere, 10);

        var covered = AreaTargeting.Cover(sphere, new GridPosition(0, 1), new GridPosition(2, 1), field);

        Assert.Contains(new GridPosition(1, 1), covered);
        Assert.Contains(new GridPosition(2, 1), covered);
        Assert.DoesNotContain(new GridPosition(4, 1), covered);
    }

    [Fact]
    public void ThePointOfOrigin_FollowsTheShape()
    {
        var caster = new GridPosition(0, 0);
        var aim = new GridPosition(5, 5);

        Assert.Equal(aim, AreaTargeting.PointOfOrigin(new EffectArea(AreaShape.Sphere, 20), caster, aim));
        Assert.Equal(aim, AreaTargeting.PointOfOrigin(new EffectArea(AreaShape.Cube, 10), caster, aim));
        Assert.Equal(caster, AreaTargeting.PointOfOrigin(new EffectArea(AreaShape.Cone, 15), caster, aim));
        Assert.Equal(caster, AreaTargeting.PointOfOrigin(new EffectArea(AreaShape.Line, 30), caster, aim));
        Assert.Equal(caster, AreaTargeting.PointOfOrigin(new EffectArea(AreaShape.Emanation, 10), caster, aim));
        Assert.Equal(caster, AreaTargeting.PointOfOrigin(null, caster, aim));
    }

    // ── Builders ────────────────────────────────────────────────────────────────

    /// <summary>An archer at (0,1) and an unarmed brute at (4,1), archer to act first.</summary>
    private static (Encounter Encounter, Combatant Archer, Combatant Brute) Shootout(
        Battlefield field,
        IReadOnlyList<int>? attackRolls = null)
    {
        var archer = CombatTestData.Combatant(
            "archer",
            stats: CombatTestData.Stats(initiativeBonus: 10, attacks: [CombatTestData.RangedAttack(bonus: 4)]),
            y: 1);

        var brute = CombatTestData.Combatant(
            "brute",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, attacks: []),
            x: 4,
            y: 1);

        var encounter = Encounter.Start(
            field,
            [archer, brute],
            new ScriptedRandomSource([20, 1, .. attackRolls ?? []]));

        return (encounter, archer, brute);
    }

    /// <summary>A single-target Dexterity save spell of Sacred Flame's rough shape.</summary>
    private static SpellDefinition DexSaveSpell() => new()
    {
        Id = "spell.test-flame",
        Name = "Test Flame",
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Cleric"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.SavingThrow,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test spell.",
        Save = new SaveEffect(
            Ability.Dexterity,
            DifficultyClass: null,
            Area: null,
            FailureDamage: [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Radiant, 7)],
            SuccessOutcome: SaveSuccessOutcome.HalfDamage,
            AppliedConditions: []),
    };

    /// <summary>A ranged spell attack.</summary>
    private static SpellDefinition AttackSpell() => new()
    {
        Id = "spell.test-ray",
        Name = "Test Ray",
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Wizard"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.Attack,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test spell.",
        IsSpellAttack = true,
        Damage = [new AttackDamage(DiceExpression.Parse("1d10"), DamageType.Fire, 5)],
    };

    /// <summary>A caster at (0,1) and a target at (4,1) on the given field.</summary>
    private static (Encounter Encounter, Combatant Target) SpellFight(
        SpellDefinition spell,
        Battlefield field,
        IReadOnlyList<int> scripted,
        bool ignoresHalfCover = false)
    {
        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            IgnoresHalfCoverOnSpellAttacks = ignoresHalfCover,
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
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 14,
                SpellAttackBonus: 6),
        };

        var caster = new Combatant("caster", "Caster", CombatTestData.Heroes, stats, new GridPosition(0, 1));

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            x: 4,
            y: 1);

        return (Encounter.Start(field, [caster, target], new ScriptedRandomSource([.. scripted])), target);
    }
}
