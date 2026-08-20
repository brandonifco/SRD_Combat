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
/// <param name="Layout">Where each side stood when initiative was rolled.</param>
public sealed record Fight(
    Encounter Encounter,
    IReadOnlyList<PartyMember> Party,
    BuiltEncounter Built,
    BattleLayout Layout = BattleLayout.Columns);

/// <summary>How a fight opens: where each side stands when initiative is rolled.</summary>
/// <remarks>
/// The SRD prints no deployment rule, so like the loot rates this is the project's own
/// design, stated here. Each shape changes the fight's geometry without touching its
/// budget: the same creatures cost the same printed XP wherever they stand. The draw is
/// level-gated — below <see cref="EncounterFactory.HordeMinimumLevel"/> every fight opens
/// as <see cref="Columns"/>, for the measured reason every count bound draws the same
/// boundary: a level 1–2 party pays for being flanked in characters removed, and an
/// ambush would rebuild the level 1 wall on purpose.
/// </remarks>
public enum BattleLayout
{
    /// <summary>Two facing columns, <see cref="EncounterFactory.StartingSeparationFeet"/> apart — the classic line battle.</summary>
    Columns,

    /// <summary>
    /// The party's column faces monsters split into two groups at the far corners, so the
    /// enemy converges from two directions instead of one and the line can be flanked.
    /// The nearest monster still starts at least the standard separation away.
    /// </summary>
    CornerGroups,

    /// <summary>
    /// The party stands in a block at the field's centre with monsters closing from all
    /// four compass points, <see cref="EncounterFactory.SurroundedSeparationFeet"/> from
    /// the block's anchor square — closer than the standard separation on purpose, since
    /// being surrounded at long range would just be four short column fights.
    /// </summary>
    Surrounded,
}

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
/// <b>Placement is a stated interpretation, and the distance is most of it.</b> The
/// sides start <see cref="StartingSeparationFeet"/> apart — facing columns at half of
/// fights, and from level 3 a seeded draw can open the fight as a
/// <see cref="BattleLayout"/> instead: monsters in two corner groups, or the party
/// surrounded at the centre (see the enum for each shape's reasoning and gates).
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
    public const int StartingSeparationFeet = 60;

    /// <summary>
    /// How far a surrounding monster starts from the party's anchor square, in feet.
    /// Half the standard separation, deliberately: a surround at 60 feet is four
    /// unhurried column fights, where at 30 the ring closes before the party has
    /// finished choosing which way to face — which is what being surrounded is.
    /// </summary>
    public const int SurroundedSeparationFeet = 30;

    /// <summary>
    /// Clear squares outside each spawn column. Two flanks of this rather than one
    /// square of shoulder room, so a creature can go round a screen instead of only
    /// through it.
    /// </summary>
    public const int MarginSquares = 3;

    /// <summary>The side identifier the monsters fight under.</summary>
    public const string MonsterSideId = "monsters";

    /// <summary>
    /// The level from which a warband rung is honoured. The same boundary
    /// <c>EncounterBuilder.MaximumFor</c> and <c>MinimumFor</c> already draw, for the
    /// same measured reason: below level 3 the cost of being outnumbered is paid in
    /// characters removed, and handing a fragile party ten enemies would rebuild the
    /// level 1 wall deliberately.
    /// </summary>
    public const int HordeMinimumLevel = 3;

    /// <summary>The fewest creatures a warband fields.</summary>
    public const int HordeMinimum = 6;

    /// <summary>
    /// The most a warband fields. Above <c>EncounterBuilder.DefaultMaximumMonsters</c>
    /// on purpose — that ceiling is what an ordinary rung tolerates, and exceeding it is
    /// the entire point of this rung.
    /// </summary>
    public const int HordeMaximum = 10;

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
    /// <param name="objective">
    /// What wins the fight, or null for last-side-standing. Resolved here rather than by
    /// the caller because a "kill the leader" rung cannot name a leader until the monsters
    /// have been drawn.
    /// </param>
    /// <param name="horde">
    /// Whether this rung is a warband: many cheap creatures on the same printed budget.
    /// Honoured only from <see cref="HordeMinimumLevel"/>; below it the request is
    /// ignored rather than refused, because the ladder is built once and cannot know
    /// what level the party will actually arrive at.
    /// </param>
    public static Fight Build(
        SrdContent content,
        IReadOnlyList<PartyMember> party,
        EncounterDifficulty difficulty,
        IRandomSource random,
        decimal maximumChallengeRating = 4m,
        ObjectiveSpec? objective = null,
        bool horde = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfZero(party.Count);

        // Each character's own level, because a party diverges once somebody dies and
        // stops earning experience.
        //
        // A boss fight fields an escort: a KillLeader rung ends when one creature dies,
        // which already makes it cheaper than the same encounter fought to the last —
        // measured at +11 full clears on its own when objectives landed — and a *lone*
        // marked creature compounds that into the easiest fight on the ladder, four
        // characters focus-firing the only enemy action economy on the field. Three is
        // leader plus a pair, and the printed budget still prices every one of them.
        // A warband fields many cheap creatures on the same printed budget, and it is
        // gated on the party being able to survive being outnumbered — the same level 3
        // boundary MaximumFor and MinimumFor already draw, and for the same measured
        // reason. Below it, the cost of being outnumbered is paid in characters removed:
        // a level 1 character has 8-12 hit points and the creatures a level 1 budget
        // buys hit for 8-9, so nearly every landed blow takes a quarter of the party's
        // action economy with it. Handing that party ten enemies would rebuild the level
        // 1 wall on purpose.
        //
        // No selection rule is needed to make the creatures cheap: the builder already
        // sizes each slot against its share of what is left, so ten slots out of one
        // Moderate budget ask for creatures a tenth of it — which is the goblin-and-
        // skeleton band the pool is thickest in. The count is the whole change.
        var lowestLevel = party.Min(member => member.Sheet.Level);
        var isHorde = horde && lowestLevel >= HordeMinimumLevel;

        var built = EncounterBuilder.ForLevels(
            MonsterPool.Draw(content.Monsters, maximumChallengeRating),
            party.Select(member => member.Sheet.Level),
            difficulty,
            random,
            maximumMonsters: isHorde ? HordeMaximum : null,
            minimumMonsters: isHorde
                ? HordeMinimum
                : objective?.Kind == ObjectiveKind.KillLeader ? 3 : null);

        var separation = StartingSeparationFeet / Battlefield.FeetPerSquare;

        // Both axes doubled (2026-08-17). The old board was 9 by 6 or so: the sides
        // stood one move apart, which is why #125 found "there are no standoff rounds
        // for a phase to spend" and why every positional experiment measured against
        // position. A field this small has no tactics in it beyond walking forward.
        //
        // Widening was measured and rejected once before, and the note left on it said
        // exactly when to come back: worth double the clears to a level 3 party and
        // ruinous to a level 1 one, so "worth revisiting the moment the level 1 wall is
        // fixed, and not before". #205 fixed it.
        //
        // MarginSquares on each flank rather than one, so going round is a real option
        // and not a squeeze along the wall.
        var width = separation + (MarginSquares * 2);
        var side = Math.Max(party.Count, Math.Max(built.Monsters.Count, 1));
        var height = (side * 2) + (MarginSquares * 2);

        var layout = DrawLayout(random, lowestLevel);
        var (partySpawns, monsterSpawns) =
            PlaceSides(layout, party.Count, built.Monsters.Count, width, height, separation);

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
            Encounter.Start(battlefield, combatants, random, Resolve(objective, built, party)),
            placed,
            built,
            layout);
    }

    /// <summary>
    /// Draws the fight's opening shape, or keeps the classic columns below the level
    /// boundary — where no die is spent at all, so a level 1–2 fight replays exactly as
    /// it did before layouts existed.
    /// </summary>
    /// <remarks>
    /// Columns stay the commonest draw at half of fights: the varied shapes are meant to
    /// be met, not to make the line battle the exception — the same reasoning that keeps
    /// a bare field a possible terrain draw.
    /// </remarks>
    private static BattleLayout DrawLayout(IRandomSource random, int lowestLevel) =>
        lowestLevel < HordeMinimumLevel
            ? BattleLayout.Columns
            : random.Roll(4) switch
            {
                1 or 2 => BattleLayout.Columns,
                3 => BattleLayout.CornerGroups,
                _ => BattleLayout.Surrounded,
            };

    /// <summary>Where each side starts, under the drawn layout.</summary>
    private static (GridPosition[] Party, GridPosition[] Monsters) PlaceSides(
        BattleLayout layout,
        int partyCount,
        int monsterCount,
        int width,
        int height,
        int separation)
    {
        switch (layout)
        {
            case BattleLayout.CornerGroups:
            {
                // The party keeps its centred column; the monsters take the far
                // column's two ends, filled alternately so the groups stay even — one
                // stack growing down from the top corner, one up from the bottom. The
                // field is sized at two of the larger side plus both margins, so the
                // stacks can never meet in the middle.
                var top = Math.Max(1, (height - partyCount) / 2);

                var partySpawns = Enumerable.Range(0, partyCount)
                    .Select(index => new GridPosition(MarginSquares, top + index))
                    .ToArray();
                var monsterSpawns = Enumerable.Range(0, monsterCount)
                    .Select(index => new GridPosition(
                        MarginSquares + separation,
                        index % 2 == 0 ? 1 + (index / 2) : height - 2 - (index / 2)))
                    .ToArray();

                return (partySpawns, monsterSpawns);
            }

            case BattleLayout.Surrounded:
            {
                var centre = new GridPosition(width / 2, height / 2);
                var ring = SurroundedSeparationFeet / Battlefield.FeetPerSquare;

                // The party stands in a block on the centre square — the anchor the
                // ring is measured from, so the block's far corner is a square nearer
                // than the stated distance rather than the stated distance being a lie.
                var partySpawns = Enumerable.Range(0, partyCount)
                    .Select(index => new GridPosition(centre.X + (index % 2), centre.Y + (index / 2)))
                    .ToArray();

                // The monsters take the four compass points in turn, each direction
                // fanning out sideways as it fills: 0, +1, -1, +2… along the axis the
                // ring does not fix. Ten monsters — the warband ceiling — reach a fan
                // of one, so nothing can collide with a neighbouring compass point.
                var monsterSpawns = Enumerable.Range(0, monsterCount)
                    .Select(index =>
                    {
                        var fan = Fan(index / 4);

                        return (index % 4) switch
                        {
                            0 => new GridPosition(centre.X + ring, centre.Y + fan),
                            1 => new GridPosition(centre.X - ring, centre.Y + fan),
                            2 => new GridPosition(centre.X + fan, centre.Y - ring),
                            _ => new GridPosition(centre.X + fan, centre.Y + ring),
                        };
                    })
                    .ToArray();

                return (partySpawns, monsterSpawns);
            }

            default:
            {
                // Both columns centred, so the flanking room is on both flanks.
                // Off-centre columns give one side a wall to hide against and the other
                // open ground, which is a difference the fight never earned.
                var side = Math.Max(partyCount, Math.Max(monsterCount, 1));
                var top = Math.Max(1, (height - side) / 2);

                var partySpawns = Enumerable.Range(0, partyCount)
                    .Select(index => new GridPosition(MarginSquares, top + index))
                    .ToArray();
                var monsterSpawns = Enumerable.Range(0, monsterCount)
                    .Select(index => new GridPosition(MarginSquares + separation, top + index))
                    .ToArray();

                return (partySpawns, monsterSpawns);
            }
        }
    }

    /// <summary>The fan sequence 0, +1, −1, +2, −2… for spreading a group along an axis.</summary>
    private static int Fan(int rank)
    {
        var magnitude = (rank + 1) / 2;

        return rank % 2 == 1 ? magnitude : -magnitude;
    }

    /// <summary>
    /// Turns a rung's <see cref="ObjectiveSpec"/> into the fight's own objective, now that
    /// there are monsters to mark.
    /// </summary>
    /// <remarks>
    /// <b>The leader is the dearest monster in the encounter, by printed XP.</b> A stated
    /// reading with two arguments: the SRD prints an XP value on every stat block and that
    /// value *is* the book's own ranking of how much creature you are facing, so the
    /// toughest thing on the field is the thing worth calling the leader; and it needs no
    /// new content, unlike a "leader" flag nothing in the SRD prints. Ties go to the
    /// earliest, so a seed always marks the same creature. Deliberately not
    /// <c>PartyDoctrine.ThreatPerRound</c>, which ranks by damage alone and would crown a
    /// glass cannon over the creature that is plainly the boss.
    /// </remarks>
    private static EncounterObjective? Resolve(
        ObjectiveSpec? objective,
        BuiltEncounter built,
        IReadOnlyList<PartyMember> party)
    {
        if (objective is null || party.Count == 0)
        {
            return null;
        }

        var sideId = party[0].Combatant.SideId;

        switch (objective.Kind)
        {
            case ObjectiveKind.SurviveRounds:
                return EncounterObjective.SurviveRounds(sideId, objective.Rounds);

            case ObjectiveKind.KillLeader:
            {
                var leader = built.Monsters
                    .Select((monster, index) => (monster, index))
                    .OrderByDescending(entry => entry.monster.ExperiencePoints)
                    .ThenBy(entry => entry.index)
                    .Select(entry => (int?)entry.index)
                    .FirstOrDefault();

                // No monsters is a degenerate encounter the caller already guards; an
                // unmarkable leader falls back to last-side-standing rather than shipping
                // an objective nothing can satisfy.
                return leader is { } index
                    ? EncounterObjective.KillLeader(sideId, $"monster{index}")
                    : null;
            }

            default:
                return null;
        }
    }
}
