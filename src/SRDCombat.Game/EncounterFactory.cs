using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>A built fight: the encounter, and what it was built from.</summary>
/// <param name="Encounter">The fight, with initiative already rolled.</param>
/// <param name="Party">The party in it.</param>
/// <param name="Built">The monsters chosen and what they cost.</param>
public sealed record Fight(Encounter Encounter, IReadOnlyList<PartyMember> Party, BuiltEncounter Built);

/// <summary>
/// Turns a party and a difficulty into a fight on a battlefield.
/// </summary>
/// <remarks>
/// <para>
/// Joins the three pieces that already existed separately: <see cref="MonsterPool"/>
/// decides which monsters may be used, <see cref="EncounterBudget"/> and
/// <see cref="EncounterBuilder"/> decide how many and which, and this places them.
/// </para>
/// <para>
/// <b>Placement is a stated interpretation, and the distance is the whole of it.</b> The
/// sides start <see cref="StartingSeparationFeet"/> apart, facing each other in columns.
/// That number decides what kind of game this is: at 5 feet every fight is a melee brawl
/// and a Longbow, a breath weapon and a Rogue's Steady Aim are all wasted; at 200 feet
/// the first two rounds are walking. Six squares is far enough that closing costs a turn
/// and ranged attacks matter on round one, and near enough that a melee creature is in
/// the fight on round two.
/// </para>
/// <para>
/// The battlefield is sized to hold both sides with room to manoeuvre round the flanks,
/// rather than being a corridor that makes positioning meaningless.
/// </para>
/// </remarks>
public static class EncounterFactory
{
    /// <summary>How far apart the two sides start, in feet.</summary>
    public const int StartingSeparationFeet = 30;

    /// <summary>The side identifier the monsters fight under.</summary>
    public const string MonsterSideId = "monsters";

    /// <summary>Builds a fight for a party at a difficulty, drawing from the curated pool.</summary>
    /// <param name="content">Loaded SRD content.</param>
    /// <param name="party">The characters, already resolved.</param>
    /// <param name="difficulty">How hard the fight should be.</param>
    /// <param name="random">The dice, seeded so the whole fight is reproducible.</param>
    /// <param name="maximumChallengeRating">
    /// The hardest creature admissible. Defaults to the tier-1 band this game is scoped
    /// to; the budget stops the fight being unfair, and this stops it containing a
    /// creature the party could not meaningfully hurt.
    /// </param>
    public static Fight Build(
        SrdContent content,
        IReadOnlyList<PartyMember> party,
        EncounterDifficulty difficulty,
        IRandomSource random,
        decimal maximumChallengeRating = 4m)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfZero(party.Count);

        // Each character's own level, because a party diverges once somebody dies and
        // stops earning experience.
        var built = EncounterBuilder.ForLevels(
            MonsterPool.Draw(content.Monsters, maximumChallengeRating),
            party.Select(member => member.Sheet.Level),
            difficulty,
            random);

        var separation = StartingSeparationFeet / Battlefield.FeetPerSquare;

        // Wide enough for both columns and the gap, deep enough for the larger side plus
        // room to go round rather than only through.
        var width = separation + 3;
        var height = Math.Max(party.Count, Math.Max(built.Monsters.Count, 1)) + 2;
        var battlefield = new Battlefield(width, height);

        var placed = party
            .Select((member, index) => member.AtPosition(new GridPosition(1, index + 1)))
            .ToArray();

        var combatants = new List<Combatant>(placed.Select(member => member.Combatant));

        foreach (var (monster, index) in built.Monsters.Select((monster, index) => (monster, index)))
        {
            combatants.Add(new Combatant(
                // Unique whatever the encounter repeats — "2 Giant Wasps" is a legal
                // encounter and both need their own identity.
                $"monster{index}",
                monster.Name,
                MonsterSideId,
                CombatantStats.FromMonster(monster),
                new GridPosition(1 + separation, index + 1)));
        }

        return new Fight(
            Encounter.Start(battlefield, combatants, random),
            placed,
            built);
    }
}
