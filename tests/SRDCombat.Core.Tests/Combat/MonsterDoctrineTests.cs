using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Squad AI slice 6 (#127), the Phase 6 split: monsters get doctrine of their own,
/// gated on the Intelligence their stat blocks already carry. A pack flanks whatever
/// its trait pays for, a tactical mind converges the way the party does, and a beast
/// stays greedy — a Boar should feel dumber than a squad.
/// </summary>
public class MonsterDoctrineTests
{
    [Fact]
    public void APackHunterTakesThePreyItsPackmateHolds()
    {
        // Both heroes are in the wolf's reach; the nearer one is also weaker, so the
        // greedy choice is heroB — but heroA has a packmate at its flank, and the
        // trait pays Advantage for exactly that.
        var wolf = Wolf("wolf", x: 2, y: 2);
        var packmate = Wolf("packmate", x: 4, y: 2);
        var heroA = Hero("heroA", hitPoints: 20, x: 3, y: 2);
        var heroB = Hero("heroB", hitPoints: 5, x: 1, y: 2);

        var encounter = Encounter.Start(
            new Battlefield(8, 5),
            [wolf, packmate, heroA, heroB],
            new ScriptedRandomSource(20, 15, 10, 5));

        var chosen = MonsterDoctrine.ChooseTarget(encounter, wolf, heroB);

        Assert.Equal("heroA", chosen?.Id);
    }

    [Fact]
    public void AWolfWithNoFlankStaysGreedy()
    {
        // The packmate is across the field from everyone: no enemy is flanked, and
        // the wolf's INT 3 leaves nothing but instinct — the caller's greedy choice
        // comes back unchanged.
        var wolf = Wolf("wolf", x: 2, y: 2);
        var packmate = Wolf("packmate", x: 7, y: 4);
        var heroA = Hero("heroA", hitPoints: 20, x: 3, y: 2);
        var heroB = Hero("heroB", hitPoints: 5, x: 1, y: 2);

        var encounter = Encounter.Start(
            new Battlefield(8, 5),
            [wolf, packmate, heroA, heroB],
            new ScriptedRandomSource(20, 15, 10, 5));

        Assert.Equal("heroB", MonsterDoctrine.ChooseTarget(encounter, wolf, heroB)?.Id);
    }

    [Fact]
    public void AnOutOfPositionPackHunterClosesOnThePackedFight()
    {
        // Nothing is in the wolf's reach. The nearer hero is unengaged; the farther
        // one is already held by a packmate — the wolf walks at the fight its pack
        // has picked rather than starting its own.
        var wolf = Wolf("wolf", x: 0, y: 0);
        var packmate = Wolf("packmate", x: 7, y: 4);
        var heroNear = Hero("heroNear", hitPoints: 20, x: 3, y: 0);
        var heroHeld = Hero("heroHeld", hitPoints: 20, x: 7, y: 3);

        var encounter = Encounter.Start(
            new Battlefield(8, 5),
            [wolf, packmate, heroNear, heroHeld],
            new ScriptedRandomSource(20, 15, 10, 5));

        Assert.Equal("heroHeld", MonsterDoctrine.ChooseTarget(encounter, wolf, heroNear)?.Id);
    }

    [Fact]
    public void ATacticalMindConvergesOnTheGlassCannon()
    {
        // Meat wall at 12 hit points and a feeble swing; glass cannon at 20 with a
        // heavy one. Greedy takes the weakest in reach — the wall — while threat per
        // hit point says the cannon's death buys more safety.
        var (encounter, brute) = BruteScenario(intelligence: 10);

        Assert.Equal(
            "cannon",
            MonsterDoctrine.ChooseTarget(
                encounter,
                brute,
                encounter.Combatants.First(c => c.Id == "wall"))?.Id);
    }

    [Fact]
    public void ABruteChargesWhatIsInFrontOfIt()
    {
        // The identical scenario at INT 5: the greedy choice comes back unchanged.
        var (encounter, brute) = BruteScenario(intelligence: 5);

        Assert.Equal(
            "wall",
            MonsterDoctrine.ChooseTarget(
                encounter,
                brute,
                encounter.Combatants.First(c => c.Id == "wall"))?.Id);
    }

    // ── The stages ──────────────────────────────────────────────────────────────

    private static (Encounter Encounter, Combatant Brute) BruteScenario(int intelligence)
    {
        var brute = CombatTestData.Combatant(
            "brute",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(intelligence: intelligence),
            x: 2,
            y: 2);

        var wall = CombatTestData.Combatant(
            "wall",
            stats: CombatTestData.Stats(
                maximumHitPoints: 12,
                attacks: [CombatTestData.MeleeAttack(damage: "1d4")]),
            x: 1,
            y: 2);

        var cannon = CombatTestData.Combatant(
            "cannon",
            stats: CombatTestData.Stats(
                maximumHitPoints: 20,
                attacks: [CombatTestData.MeleeAttack(damage: "2d10 + 5")]),
            x: 3,
            y: 2);

        var encounter = Encounter.Start(
            new Battlefield(8, 5),
            [brute, wall, cannon],
            new ScriptedRandomSource(20, 10, 5));

        return (encounter, brute);
    }

    private static Combatant Wolf(string id, int x, int y) =>
        CombatTestData.Combatant(
            id,
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(intelligence: 3) with
            {
                Entries = [new MonsterEntry("Pack Tactics", MonsterEntrySection.Trait, "...")],
            },
            x: x,
            y: y);

    private static Combatant Hero(string id, int hitPoints, int x, int y) =>
        CombatTestData.Combatant(
            id,
            stats: CombatTestData.Stats(maximumHitPoints: hitPoints),
            x: x,
            y: y);
}
