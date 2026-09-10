using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="PlayMode.SpellRangeEnvelope"/>, <see cref="PlayMode.SpellOutOfRangeCode"/>
/// and <see cref="PlayMode.AreaCoverage"/> (#302) — the plain-value seams behind the
/// board's armed-spell range and area previews. The area half is checked against
/// <see cref="AreaTargeting.Cover"/> directly, the identical call
/// <c>Encounter.CastSpell</c>'s own <c>SaveVictims</c> makes, so this cannot pass merely
/// because the production seam and this test share a typo about what the geometry
/// should be.
/// </summary>
public class AreaCoverageTests
{
    private static readonly IReadOnlySet<GridPosition> NoFog = new HashSet<GridPosition>();

    private static Combatant Caster(int x, int y, string sideId = FightTestData.Heroes) =>
        new("Caster", "Caster", sideId, FightTestData.Stats(), new GridPosition(x, y));

    private static Combatant Bystander(string id, int x, int y, string sideId) =>
        new(id, id, sideId, FightTestData.Stats(), new GridPosition(x, y));

    /// <summary>Fireball's own printed shape: a 20-foot-radius Sphere, 150 feet range, every creature.</summary>
    private static SpellDefinition SphereSpell() => FightTestData.AnySpell("Fireball-like") with
    {
        RangeText = "150 feet",
        RangeFeet = 150,
        Mechanics = EntryMechanics.SavingThrow,
        Save = new SaveEffect(
            Ability.Dexterity,
            DifficultyClass: 15,
            Area: new EffectArea(AreaShape.Sphere, SizeFeet: 20),
            FailureDamage: [],
            SuccessOutcome: SaveSuccessOutcome.HalfDamage,
            AppliedConditions: []),
    };

    /// <summary>Burning Hands' own printed shape: a self-ranged 15-foot Cone, every creature.</summary>
    private static SpellDefinition ConeSpell() => FightTestData.AnySpell("Burning-Hands-like") with
    {
        RangeText = "Self",
        RangeFeet = null,
        Mechanics = EntryMechanics.SavingThrow,
        Save = new SaveEffect(
            Ability.Dexterity,
            DifficultyClass: 15,
            Area: new EffectArea(AreaShape.Cone, SizeFeet: 15),
            FailureDamage: [],
            SuccessOutcome: SaveSuccessOutcome.HalfDamage,
            AppliedConditions: []),
    };

    // ---- SpellRangeEnvelope --------------------------------------------------------

    [Fact]
    public void SelfRangedSpellHasNoEnvelope()
    {
        var field = new Battlefield(20, 20);
        var caster = Caster(10, 10);

        var envelope = PlayMode.SpellRangeEnvelope(field, caster, ConeSpell());

        Assert.Empty(envelope);
    }

    [Fact]
    public void SelfRangedSpellHasNoEnvelopeEvenWhenRangeFeetIsSomehowSet()
    {
        // A defensive, independent check: IsSelfRanged reads RangeText, and
        // TargetRangeFeet reads RangeFeet — the corpus never actually sets both at
        // once (no printed "Self" spell also carries a parsed distance), but
        // SpellRangeEnvelope does not lean on that correlation holding forever. This
        // fixture breaks it on purpose, proving the self-ranged guard is asked in its
        // own right rather than merely inferred from RangeFeet already being null.
        var field = new Battlefield(20, 20);
        var caster = Caster(10, 10);
        var spell = FightTestData.AnySpell() with { RangeText = "Self", RangeFeet = 60 };

        var envelope = PlayMode.SpellRangeEnvelope(field, caster, spell);

        Assert.Empty(envelope);
    }

    [Fact]
    public void RangedSpellEnvelopeIsExactlyItsPrintedDistance()
    {
        var field = new Battlefield(40, 40);
        var caster = Caster(20, 20);
        var spell = FightTestData.AnySpell() with { RangeText = "60 feet", RangeFeet = 60 };

        var envelope = PlayMode.SpellRangeEnvelope(field, caster, spell);

        // 60 ft = 12 squares (Chebyshev), unclipped on a board this size.
        Assert.Equal(25 * 25, envelope.Count);
        Assert.Contains(new GridPosition(32, 20), envelope); // exactly 60 ft east
        Assert.DoesNotContain(new GridPosition(33, 20), envelope); // 65 ft
    }

    [Fact]
    public void UnlimitedRangeSpellHasNoEnvelope()
    {
        // Sight and Unlimited both leave RangeFeet null and are not Touch or Self —
        // TargetRangeFeet is null, and a wash over the whole board would say nothing a
        // player does not already know from the spell's own printed range text.
        var field = new Battlefield(20, 20);
        var caster = Caster(10, 10);
        var spell = FightTestData.AnySpell() with { RangeText = "Sight", RangeFeet = null };

        var envelope = PlayMode.SpellRangeEnvelope(field, caster, spell);

        Assert.Empty(envelope);
    }

    // ---- SpellOutOfRangeCode --------------------------------------------------------

    [Fact]
    public void SelfRangedSpellIsNeverOutOfRange() =>
        Assert.Null(PlayMode.SpellOutOfRangeCode(ConeSpell(), distanceFeet: 1000));

    [Fact]
    public void WithinRangeIsNotOutOfRange()
    {
        var spell = FightTestData.AnySpell() with { RangeText = "60 feet", RangeFeet = 60 };

        Assert.Null(PlayMode.SpellOutOfRangeCode(spell, distanceFeet: 60));
    }

    [Fact]
    public void BeyondRangeNamesSpellOutOfRange()
    {
        var spell = FightTestData.AnySpell() with { RangeText = "60 feet", RangeFeet = 60 };

        Assert.Equal("spell.out_of_range", PlayMode.SpellOutOfRangeCode(spell, distanceFeet: 65));
    }

    // ---- AreaCoverage: Sphere ---------------------------------------------------------

    [Fact]
    public void SphereCoverageEqualsAreaTargetingForTheSameOrigin()
    {
        var field = new Battlefield(40, 40);
        var caster = Caster(20, 20);
        var spell = SphereSpell();
        var aim = new GridPosition(25, 20); // 25 ft away, within the 150 ft range

        var expected = AreaTargeting.Cover(spell.Save!.Area!, caster.Position, aim, field);
        var preview = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, aim, [], NoFog);

        Assert.False(preview.OutOfRange);
        Assert.NotEmpty(expected);
        Assert.Equal(expected.OrderBy(s => s.X).ThenBy(s => s.Y), preview.Squares.OrderBy(s => s.X).ThenBy(s => s.Y));
    }

    [Fact]
    public void SphereCoverageCatchesAVisibleCreatureInsideIt()
    {
        var field = new Battlefield(40, 40);
        var caster = Caster(20, 20, FightTestData.Heroes);
        var victim = Bystander("Victim", 25, 20, FightTestData.Monsters); // at the aim point itself
        var spell = SphereSpell();
        var aim = new GridPosition(25, 20);

        var preview = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, aim, [caster, victim], NoFog);

        Assert.Contains(victim.Id, preview.CaughtCombatantIds);
    }

    [Fact]
    public void AHiddenCreatureInsideCoverageIsNotMarkedCaught()
    {
        var field = new Battlefield(40, 40);
        var caster = Caster(20, 20, FightTestData.Heroes);
        var victim = Bystander("Victim", 25, 20, FightTestData.Monsters);
        var spell = SphereSpell();
        var aim = new GridPosition(25, 20);
        var fog = new HashSet<GridPosition> { victim.Position };

        var preview = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, aim, [caster, victim], fog);

        Assert.DoesNotContain(victim.Id, preview.CaughtCombatantIds);

        // Opposite polarity: the very same victim, visible, is caught — proving the
        // absence above is the fog filter's own doing and not a fixture that never
        // caught this victim at all.
        var withoutFog = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, aim, [caster, victim], NoFog);
        Assert.Contains(victim.Id, withoutFog.CaughtCombatantIds);
    }

    [Fact]
    public void AFoggedSquareInsideCoverageIsDroppedFromWhatIsDrawn()
    {
        var field = new Battlefield(40, 40);
        var caster = Caster(20, 20);
        var spell = SphereSpell();
        var aim = new GridPosition(25, 20);

        var expected = AreaTargeting.Cover(spell.Save!.Area!, caster.Position, aim, field);
        Assert.Contains(aim, expected); // a Sphere keeps its own centre square

        var fog = new HashSet<GridPosition> { aim };
        var preview = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, aim, [], fog);

        Assert.DoesNotContain(aim, preview.Squares);
        Assert.Equal(expected.Count - 1, preview.Squares.Count);
    }

    [Fact]
    public void EnemiesOnlyAreaExcludesTheCastersOwnSide()
    {
        var field = new Battlefield(40, 40);
        var caster = Caster(20, 20, FightTestData.Heroes);
        var ally = Bystander("Ally", 25, 20, FightTestData.Heroes);
        var enemy = Bystander("Enemy", 24, 20, FightTestData.Monsters);
        var spell = SphereSpell() with
        {
            Save = SphereSpell().Save! with { Area = SphereSpell().Save!.Area! with { EnemiesOnly = true } },
        };
        var aim = new GridPosition(25, 20);

        var preview = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, aim, [caster, ally, enemy], NoFog);

        Assert.Contains(enemy.Id, preview.CaughtCombatantIds);
        Assert.DoesNotContain(ally.Id, preview.CaughtCombatantIds);
    }

    [Fact]
    public void BeyondTheSpellsOwnRangeReportsOutOfRangeAndCoversNothing()
    {
        var field = new Battlefield(80, 80);
        var caster = Caster(30, 30);
        var spell = SphereSpell(); // 150 ft range

        // Placed well past the printed 150 ft range: 40 squares = 200 ft.
        var farAim = new GridPosition(70, 30);
        Assert.True(caster.DistanceFeetTo(farAim) > 150);

        var preview = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, farAim, [], NoFog);

        Assert.True(preview.OutOfRange);
        Assert.Empty(preview.Squares);
        Assert.Empty(preview.CaughtCombatantIds);
    }

    // ---- AreaCoverage: Cone (self-ranged, never out of range) ------------------------

    [Fact]
    public void SelfRangedConeIsNeverOutOfRangeHoweverFarTheAimIs()
    {
        var field = new Battlefield(60, 60);
        var caster = Caster(30, 30);
        var spell = ConeSpell();
        var farAim = new GridPosition(59, 30);

        var preview = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, farAim, [], NoFog);

        Assert.False(preview.OutOfRange);
    }

    [Fact]
    public void ConeCoverageEqualsAreaTargetingForTheSameOriginAndAim()
    {
        var field = new Battlefield(40, 40);
        var caster = Caster(20, 20);
        var spell = ConeSpell();
        var aim = new GridPosition(23, 20); // due east — gives the cone its direction

        var expected = AreaTargeting.Cover(spell.Save!.Area!, caster.Position, aim, field);
        var preview = PlayMode.AreaCoverage(field, caster, spell, spell.Save!.Area!, aim, [], NoFog);

        Assert.NotEmpty(expected);
        Assert.Equal(expected.OrderBy(s => s.X).ThenBy(s => s.Y), preview.Squares.OrderBy(s => s.X).ThenBy(s => s.Y));
    }

    // ---- TargetingPreviewMayShow ------------------------------------------------------

    [Fact]
    public void ArmedWithNoOverlayMayShow() =>
        Assert.True(PlayMode.TargetingPreviewMayShow(armed: true, overOverlay: false));

    [Fact]
    public void NotArmedMayNotShow() =>
        Assert.False(PlayMode.TargetingPreviewMayShow(armed: false, overOverlay: false));

    [Fact]
    public void ArmedOverAnOverlayMayNotShow() =>
        Assert.False(PlayMode.TargetingPreviewMayShow(armed: true, overOverlay: true));

    [Fact]
    public void NotArmedOverAnOverlayMayNotShow() =>
        Assert.False(PlayMode.TargetingPreviewMayShow(armed: false, overOverlay: true));
}
