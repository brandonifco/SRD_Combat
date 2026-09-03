using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Who an armed action may be pointed at, and in what order.
/// </summary>
/// <remarks>
/// A convenience rather than a rule — the engine still refuses anything this admits by
/// mistake — so what is worth pinning is the ordering the cursor and Tab depend on, and
/// the edges where a wrong answer costs a player something: an out-of-reach enemy offered
/// for a blade, and a corpse offered for anything.
/// </remarks>
public class TargetChoiceTests
{
    [Fact]
    public void AttackTargetsAreEnemiesNearestFirst()
    {
        var (encounter, actor) = Fight();

        var targets = TargetChoice.For(encounter, actor, TargetKind.Attack, attack: Bow);

        // Both enemies, the closer one first; no allies, no self.
        Assert.Equal(["near", "far"], targets.Select(target => target.Id));
    }

    [Fact]
    public void AMeleeAttackOffersOnlyWhatItReaches()
    {
        // The predicate is CombatAttack.CanReach — the very method Encounter.Attack
        // refuses on — so the offer and the refusal cannot disagree.
        var (encounter, actor) = Fight();

        var targets = TargetChoice.For(encounter, actor, TargetKind.Attack, attack: Sword);

        Assert.Equal(["near"], targets.Select(target => target.Id));
    }

    [Fact]
    public void TheDeadAreNobodysTarget()
    {
        var (encounter, actor) = Fight();

        DamageRules.Apply(
            encounter.Combatants.Single(combatant => combatant.Id == "near"),
            1_000,
            DamageType.Slashing);

        var targets = TargetChoice.For(encounter, actor, TargetKind.Attack, attack: Bow);

        Assert.Equal(["far"], targets.Select(target => target.Id));
    }

    [Fact]
    public void CyclingWalksTheRingAndWrapsRound()
    {
        var (encounter, actor) = Fight();
        var targets = TargetChoice.For(encounter, actor, TargetKind.Attack, attack: Bow);

        // Nothing selected yet lands on the nearest, which is where the cursor starts.
        Assert.Equal("near", TargetChoice.Next(targets, null)?.Id);
        Assert.Equal("far", TargetChoice.Next(targets, "near")?.Id);

        // And round again, so Tab never dead-ends.
        Assert.Equal("near", TargetChoice.Next(targets, "far")?.Id);
    }

    [Theory]
    [InlineData(EntryMechanics.Attack, Monsters)]
    [InlineData(EntryMechanics.SavingThrow, Monsters)]
    [InlineData(EntryMechanics.Healing, Party)]
    public void ASpellsShapeDecidesWhichSideItAimsAt(EntryMechanics mechanics, string wanted)
    {
        // Found by casting Guiding Bolt: range alone made every ally a candidate and the
        // caster the nearest of them, at distance zero, so arming a spell attack left the
        // cursor where it already was and read as no default at all.
        var (encounter, actor) = Fight();

        var targets = TargetChoice.For(encounter, actor, TargetKind.Spell, spell: Spell(mechanics));

        Assert.NotEmpty(targets);
        Assert.All(targets, target => Assert.Equal(wanted, target.SideId));
    }

    [Fact]
    public void ASpellOfAnUnrecognisedShapeStaysGenerous()
    {
        // An unfamiliar shape is a reason to offer more rather than less: the engine
        // still rules, and a candidate it refuses costs only a message.
        var (encounter, actor) = Fight();

        var targets = TargetChoice.For(encounter, actor, TargetKind.Spell, spell: Spell(EntryMechanics.Unmodelled));

        Assert.Contains(targets, target => target.SideId == Party);
        Assert.Contains(targets, target => target.SideId == Monsters);
    }

    [Fact]
    public void CyclingAnEmptyRingIsNotAnError()
    {
        Assert.Null(TargetChoice.Next([], null));
        Assert.Null(TargetChoice.Next([], "anybody"));
    }

    [Fact]
    public void SparkHealFindsAlliesAndSparkHarmFindsEnemies()
    {
        var (encounter, actor) = Fight();

        Assert.All(
            TargetChoice.For(encounter, actor, TargetKind.SparkHeal),
            target => Assert.Equal(Party, target.SideId));

        Assert.All(
            TargetChoice.For(encounter, actor, TargetKind.SparkHarm),
            target => Assert.Equal(Monsters, target.SideId));
    }

    [Fact]
    public void APotionNeedsAFlaskSomewhere()
    {
        // Nobody is carrying anything, so there is nothing to point at — which is the
        // half of the rule a player actually meets, since the row offers Give Potion
        // only when somebody in reach has one. Stocking a pack needs InventoryState.Seed,
        // which is internal to Core, so the carrying case is left to Core's own tests
        // rather than reached for through a back door.
        var (encounter, actor) = Fight();

        Assert.Empty(TargetChoice.For(encounter, actor, TargetKind.Potion));
    }

    [Fact]
    public void TradeTargetsAreLivingAlliesWithinReachExcludingSelfEnemiesAndDistantAllies()
    {
        // Unlike Potion, ownership plays no part: the point is redistributing what the
        // actor already carries, so an ally offering nothing of their own is still a
        // legal recipient.
        var (encounter, actor, ally, _) = TradeFight();

        var targets = TargetChoice.For(encounter, actor, TargetKind.Trade);

        Assert.Equal([ally.Id], targets.Select(target => target.Id));
    }

    [Fact]
    public void AnUnconsciousAllyIsStillATradeTarget()
    {
        var (encounter, actor, ally, _) = TradeFight();
        DamageRules.Apply(ally, ally.Stats.MaximumHitPoints, DamageType.Slashing);

        Assert.True(ally.HasCondition(ConditionType.Unconscious));
        Assert.Contains(
            TargetChoice.For(encounter, actor, TargetKind.Trade),
            target => target.Id == ally.Id);
    }

    [Fact]
    public void ADeadAllyIsNotATradeTarget()
    {
        var (encounter, actor, ally, _) = TradeFight();
        DamageRules.Apply(ally, ally.Stats.MaximumHitPoints, DamageType.Slashing);
        DamageRules.Apply(ally, ally.Stats.MaximumHitPoints, DamageType.Slashing);

        Assert.True(ally.IsDead);
        Assert.DoesNotContain(
            TargetChoice.For(encounter, actor, TargetKind.Trade),
            target => target.Id == ally.Id);
    }

    private const string Party = "party";
    private const string Monsters = "monsters";

    private static CombatAttack Sword =>
        new("Longsword", AttackKind.Melee, 5, 5, null, null,
            [new AttackDamage(DiceExpression.Parse("1d8 + 3"), DamageType.Slashing, 7)]);

    private static SpellDefinition Spell(EntryMechanics mechanics) => new()
    {
        Id = "spell.test",
        Name = "Test",
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Cleric"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        RangeText = "120 feet",
        RangeFeet = 120,
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Text = "Test",
        Mechanics = mechanics,
        SourcePage = 1,
    };

    private static CombatAttack Bow =>
        new("Shortbow", AttackKind.Ranged, 5, null, 80, 320,
            [new AttackDamage(DiceExpression.Parse("1d6 + 3"), DamageType.Piercing, 6)]);

    /// <summary>A hero and an ally, an enemy adjacent, and another across the field.</summary>
    private static (Encounter Encounter, Combatant Actor) Fight()
    {
        var encounter = Encounter.Start(
            new Battlefield(20, 12),
            [
                Combatant("hero", Party, 0, 0, initiative: 10),
                Combatant("ally", Party, 0, 1),
                Combatant("near", Monsters, 1, 0),
                Combatant("far", Monsters, 8, 0),
            ],
            new SeededRandomSource(5));

        return (encounter, encounter.Combatants.Single(combatant => combatant.Id == "hero"));
    }

    /// <summary>
    /// A hero, an adjacent ally, a distant ally 30 feet off, and an adjacent enemy —
    /// Trade's own candidate rules read allegiance and distance, not who carries what.
    /// </summary>
    private static (Encounter Encounter, Combatant Actor, Combatant Ally, Combatant Distant) TradeFight()
    {
        var encounter = Encounter.Start(
            new Battlefield(20, 12),
            [
                Combatant("hero", Party, 0, 0, initiative: 10),
                Combatant("ally", Party, 0, 1),
                Combatant("distant", Party, 0, 6),
                Combatant("near", Monsters, 1, 0),
            ],
            new SeededRandomSource(5));

        return (
            encounter,
            encounter.Combatants.Single(combatant => combatant.Id == "hero"),
            encounter.Combatants.Single(combatant => combatant.Id == "ally"),
            encounter.Combatants.Single(combatant => combatant.Id == "distant"));
    }

    private static Combatant Combatant(string id, string side, int x, int y, int initiative = -10)
    {
        var abilities = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => new MonsterAbility(12, 1));

        return new Combatant(
            id,
            id,
            side,
            new CombatantStats(
                14, 30, 30, initiative, abilities, 2, CreatureSize.Medium,
                new Dictionary<DamageType, DamageResponse>(), [], [Sword],
                DiesAtZeroHitPoints: side == Monsters),
            new GridPosition(x, y));
    }
}
