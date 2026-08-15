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
/// rather than being a corridor that makes positioning meaningless — and it is not bare:
/// <see cref="TerrainGenerator"/> scatters walls and Difficult Terrain between the sides,
/// seeded from the same dice as everything else, with its own interpretations stated on
/// the class.
/// </para>
/// <para>
/// <b>Six squares is exactly one move, and widening it was measured and rejected —
/// conditionally (2026-08-15).</b> A standard Speed of 30 crosses the whole gap on round
/// one, so there is no approach to make and a bow's 80/320-foot range is spent before the
/// first die is rolled. That is the best available explanation for the entire squad-AI
/// series measuring *against* position, and #125 had already written the mechanism down:
/// "the sides start one move apart, so there are no standoff rounds for a phase to spend."
/// So the obvious fix was tried — separation raised, depth raised to keep the field from
/// becoming a corridor, both columns centred so the flanking room is on both flanks.
/// Measured on <c>tools/PacingMeasure</c>, seeds 1-120, loot on, one build:
/// </para>
/// <para>
/// From level 1 — 30 ft: median 24, 54 clears. 45 ft: median 4, 30 clears. 60 ft:
/// median 8, 42 clears.<br/>
/// From level 3 — 30 ft: median 13, 27 clears. 45 ft: median 18, 36 clears. 60 ft:
/// median 23.5, 45 clears.
/// </para>
/// <para>
/// <b>The sign flips with the party's level, which is the finding.</b> A wider field is
/// worth nearly double the clears to a level 3 party (27 to 45) and costs a level 1 party
/// most of its run. Every variant ended in defeat and not one in the policy's round limit
/// — the new <c>ended:</c> line in the instrument is what proves that, so this is
/// lethality and not a battlefield the pathfinder cannot cross. The mechanism is that
/// crossing open ground costs a round in which the party's melee contributes nothing while
/// anything with a ranged attack shoots for free, and a level 1 party has no hit points to
/// spend on that round. So the board is *not* the first problem: the opening is (#83, and
/// the 35-of-120 runs that die by fight 4 on the shipped board). <b>Widening is worth
/// revisiting the moment the level 1 wall is fixed, and not before</b> — it is one
/// constant, and the ladder above is the bar to beat. Depth and centring alone, at 30 ft,
/// measured 22/51 against the baseline's 24/54: inside noise, no benefit, not shipped.
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

        var partySpawns = party
            .Select((_, index) => new GridPosition(1, index + 1))
            .ToArray();
        var monsterSpawns = built.Monsters
            .Select((_, index) => new GridPosition(1 + separation, index + 1))
            .ToArray();

        var battlefield = TerrainGenerator.Generate(
            width,
            height,
            [.. partySpawns, .. monsterSpawns],
            random);

        var placed = party
            .Select((member, index) => member.AtPosition(partySpawns[index]))
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
                monsterSpawns[index]));
        }

        return new Fight(
            Encounter.Start(battlefield, combatants, random),
            placed,
            built);
    }
}
