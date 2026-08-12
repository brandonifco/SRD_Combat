using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Game.Tests;

/// <summary>
/// One unique letter per combatant.
/// </summary>
/// <remarks>
/// Written after the first fight ever played surfaced the bug: an Animated Flying Sword,
/// an Ape and a Cleric called Aldous all drew as <c>A</c>, so the grid was ambiguous and
/// naming a target would have hit whichever the search reached first. Uniqueness is the
/// property, and it has to hold for adversarial names rather than merely for the party
/// that happened to be on screen.
/// </remarks>
public class LabelsTests
{
    [Fact]
    public void CollidingNamesStillGetDistinctLabels()
    {
        var combatants = Fight("Animated Flying Sword", "Ape", "Aldous", "Aardvark");

        var labels = Labels.For(combatants);
        var assigned = combatants.Select(labels.Of).ToArray();

        Assert.Equal(assigned.Length, assigned.Distinct().Count());
    }

    [Fact]
    public void ALabelComesFromTheNameWhereverOneIsFree()
    {
        // Mnemonic where it can be: the first free letter of the creature's own name.
        var combatants = Fight("Sable", "Korrin", "Goblin");
        var labels = Labels.For(combatants);

        Assert.Equal('S', labels.Of(combatants[0]));
        Assert.Equal('K', labels.Of(combatants[1]));
        Assert.Equal('G', labels.Of(combatants[2]));
    }

    [Fact]
    public void ALaterCollisionFallsBackToAnotherLetterOfItsOwnName()
    {
        var combatants = Fight("Ape", "Aldous");
        var labels = Labels.For(combatants);

        Assert.Equal('A', labels.Of(combatants[0]));

        // 'A' is taken, so Aldous takes the next letter it owns rather than an unrelated one.
        Assert.Equal('L', labels.Of(combatants[1]));
    }

    [Fact]
    public void AMatchWorksByLabelOrByName()
    {
        var combatants = Fight("Ape", "Aldous");
        var labels = Labels.For(combatants);

        Assert.True(labels.Matches(combatants[0], "a"));
        Assert.True(labels.Matches(combatants[0], "Ape"));
        Assert.True(labels.Matches(combatants[1], "l"));
        Assert.True(labels.Matches(combatants[1], "Ald"));

        // The label is what disambiguates: "a" is the Ape, not Aldous.
        Assert.False(labels.Matches(combatants[1], "a"));
    }

    [Fact]
    public void MoreCombatantsThanLettersStillTerminates()
    {
        // Nobody will field 30 creatures, but a labeller that loops or throws on a full
        // alphabet would fail at the worst possible moment.
        var combatants = Fight([.. Enumerable.Range(0, 30).Select(index => $"Goblin {index}")]);

        var labels = Labels.For(combatants);

        Assert.All(combatants, combatant => Assert.NotEqual('\0', labels.Of(combatant)));
    }

    private static Combatant[] Fight(params string[] names) =>
        names.Select((name, index) => new Combatant(
                $"c{index}",
                name,
                "side",
                new CombatantStats(
                    12,
                    10,
                    30,
                    0,
                    new Dictionary<Ability, MonsterAbility>(),
                    2,
                    CreatureSize.Medium,
                    new Dictionary<DamageType, DamageResponse>(),
                    [],
                    [],
                    DiesAtZeroHitPoints: true),
                new GridPosition(index, 0)))
            .ToArray();
}
