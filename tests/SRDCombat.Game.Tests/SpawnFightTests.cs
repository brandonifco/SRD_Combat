using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// <see cref="EncounterFactory.BuildChosen"/> (#456): a hand-picked cast stands on
/// exactly the board a budgeted one would — placed, terrained, and honestly priced.
/// </summary>
public class SpawnFightTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    private static MonsterDefinition Named(string name) =>
        Content.Monsters.Single(monster => monster.Name == name);

    [Fact]
    public void FieldsExactlyTheChosenCast()
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var roster = new[] { Named("Ogre"), Named("Goblin Warrior"), Named("Goblin Warrior") };

        var fight = EncounterFactory.BuildChosen(party, roster, new SeededRandomSource(42));

        Assert.Equal(
            ["Ogre", "Goblin Warrior", "Goblin Warrior"],
            fight.Built.Monsters.Select(monster => monster.Name).ToArray());

        var monsters = fight.Encounter.Combatants
            .Where(combatant => combatant.SideId == EncounterFactory.MonsterSideId)
            .ToArray();

        Assert.Equal(3, monsters.Length);
        Assert.Equal(3, monsters.Select(combatant => combatant.Id).Distinct().Count());
        Assert.Equal(party.Count + 3, fight.Encounter.Combatants.Count);
    }

    [Fact]
    public void PricesTheCastAtItsPrintedExperienceWithNoPretendedHeadroom()
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var roster = new[] { Named("Ogre"), Named("Wolf") };

        var fight = EncounterFactory.BuildChosen(party, roster, new SeededRandomSource(7));

        var printed = roster.Sum(monster => monster.ExperiencePoints);
        Assert.Equal(printed, fight.Built.Budget);
        Assert.Equal(printed, fight.Built.Spent);
        Assert.Equal(0, fight.Built.Remaining);
    }

    [Fact]
    public void EveryBodyStandsOnTheBoard()
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var roster = Enumerable.Repeat(Named("Wolf"), 10).ToArray();

        var fight = EncounterFactory.BuildChosen(party, roster, new SeededRandomSource(3));
        var battlefield = fight.Encounter.Battlefield;

        // Every occupied square (not just the anchor) has to fit the board — with a
        // real footprint, the far corner (anchor + span - 1) is the square that can
        // hang off the edge.
        Assert.All(
            fight.Encounter.Combatants.SelectMany(combatant => combatant.Space.Squares()),
            square =>
            {
                Assert.InRange(square.X, 0, battlefield.Width - 1);
                Assert.InRange(square.Y, 0, battlefield.Height - 1);
            });

        // Bodies, not anchors: two combatants with distinct anchors can still overlap
        // once a footprint spans more than one square (#429's final slice). Every
        // Wolf here is SpaceSize.Medium today, so a space is exactly its anchor square
        // and this is equivalent to the old anchor-distinctness assert — but it is
        // now expressed over occupied squares, so it keeps meaning what it says once
        // spans grow (#465).
        var spaces = fight.Encounter.Combatants.Select(combatant => combatant.Space).ToArray();
        for (var i = 0; i < spaces.Length; i++)
        {
            for (var j = i + 1; j < spaces.Length; j++)
            {
                Assert.False(
                    spaces[i].Overlaps(spaces[j]),
                    $"combatants {i} and {j} occupy overlapping squares: " +
                    $"{string.Join(", ", spaces[i].Squares())} vs {string.Join(", ", spaces[j].Squares())}");
            }
        }
    }

    /// <summary>
    /// A bare combatant with an explicit <see cref="CombatantStats.SpaceSize"/> — the
    /// #429 scaffold field tests set to exercise footprint machinery that no fielded
    /// roster reaches yet (every printed cast today resolves to Medium).
    /// </summary>
    private static Combatant CombatantAt(GridPosition anchor, CreatureSize spaceSize)
    {
        var abilities = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => new MonsterAbility(10, 0));

        return new Combatant(
            $"{spaceSize}@{anchor.X},{anchor.Y}",
            spaceSize.ToString(),
            "monsters",
            new CombatantStats(
                13, 11, 40, 2, abilities, 2, spaceSize,
                new Dictionary<DamageType, DamageResponse>(), [],
                [new CombatAttack("Slam", AttackKind.Melee, 4, 5, null, null,
                    [new AttackDamage(DiceExpression.Parse("2d6 + 2"), DamageType.Bludgeoning, 9)])],
                DiesAtZeroHitPoints: true)
            {
                SpaceSize = spaceSize,
            },
            anchor);
    }

    /// <summary>
    /// Pins the body-square-overlap predicate <see cref="EveryBodyStandsOnTheBoard"/>
    /// relies on, directly against a fabricated multi-square footprint — the fielded
    /// cast stays Medium today (and even after #429's final slice, this suite's own
    /// roster may never draw a Large monster), so nothing else in this file ever
    /// exercises two footprints wide enough to actually overlap without sharing an
    /// anchor. Without this test, the disjointness check could regress to
    /// anchor-distinctness (or <c>Overlaps</c> itself could break) and every committed
    /// test would keep passing (#465, qc finding on PR review).
    /// </summary>
    [Fact]
    public void LargeFootprintsWithDistinctAnchorsCanStillOverlap()
    {
        // Large is a 2x2 block. Anchors (0,0) and (1,0) differ, but the blocks share
        // squares (1,0) and (1,1) — exactly the shape a distinct-anchors check misses.
        var first = CombatantAt(new GridPosition(0, 0), CreatureSize.Large);
        var adjacent = CombatantAt(new GridPosition(1, 0), CreatureSize.Large);

        Assert.NotEqual(first.Position, adjacent.Position);
        Assert.True(first.Space.Overlaps(adjacent.Space));

        // Placed clear of each other (a two-square gap between the 2x2 blocks), the
        // same predicate must report no overlap — the check flags real collisions, not
        // every pair of same-sized creatures.
        var farEnough = CombatantAt(new GridPosition(4, 0), CreatureSize.Large);
        Assert.False(first.Space.Overlaps(farEnough.Space));
    }

    [Fact]
    public void ASingleMonsterAndALargeOneBothPlace()
    {
        var party = PregeneratedParty.Build(Content, level: 1);

        // Ogre is printed Large — under the interim single-square reading it still
        // spans one square (#429's dark scaffold), so this pins the call not crashing
        // and stays valid when the final slice turns real footprints on.
        var fight = EncounterFactory.BuildChosen(party, [Named("Ogre")], new SeededRandomSource(11));

        Assert.Single(fight.Built.Monsters);
        Assert.False(fight.Encounter.IsComplete);
    }
}
