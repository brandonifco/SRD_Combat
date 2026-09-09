using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.PacingMeasure;

/// <summary>Why a run stopped, so that defeat and an unresolvable fight stay distinct.</summary>
public enum RunEnd
{
    /// <summary>The ladder ran out — the party survived everything put in front of it.</summary>
    Cleared,

    /// <summary>The party was defeated.</summary>
    Defeated,

    /// <summary>The policy's round limit fired: a fight that could not resolve.</summary>
    Stalled,

    /// <summary>The builder produced no monsters, which should not happen.</summary>
    NoFight,
}

/// <summary>
/// One completed encounter's figures, plus whether the party actually won it.
/// </summary>
/// <remarks>
/// <see cref="Won"/> is the fix for #707: the loop appends a row for every encounter
/// that finishes (so the per-band and by-monster-count reports have something to say
/// about a party wipe's hp-left and downed count too), but a defeat is not a won fight
/// and must never be counted or averaged under a "won"/"cleared" label. Read
/// <see cref="Encounter.WinningSide"/> right after <see cref="Encounter.IsComplete"/>
/// goes true — it is set in the same step — rather than from
/// <c>GauntletRun.Outcome</c>, which reflects the whole run (a defeat there also means
/// this fight's <c>WinningSide</c> was not the party's side; the two never disagree,
/// but this field is the one that means "this specific fight").
/// </remarks>
public sealed record FightRecord(
    int Fight,
    string Difficulty,
    double HpLeft,
    int Downed,
    int Rounds,
    int Monsters,
    bool Won);

/// <summary>One early death: which fight, against what, and how hard it hit.</summary>
public sealed record DeathRecord(int Fight, string Difficulty, int Count, int Biggest);

/// <summary>One seed's whole run: how far it got, and every fight and death along the way.</summary>
public sealed record SeedResult(
    int Cleared,
    int Level,
    RunEnd End,
    IReadOnlyList<FightRecord> Fights,
    IReadOnlyList<DeathRecord> Deaths);

/// <summary>
/// The pacing instrument's one-seed core loop, extracted from <c>Program.cs</c> so
/// <c>tests/PacingMeasure.Tests</c> can drive it directly rather than only through the
/// executable's console output (#707).
/// </summary>
public static class PacingRun
{
    /// <summary>Plays one seed's run to its end and reports every fight and death.</summary>
    /// <param name="content">The loaded SRD content.</param>
    /// <param name="ladder">The ladder to climb — <see cref="GauntletLadder.Default"/> for the canonical form.</param>
    /// <param name="startLevel">The party's starting level.</param>
    /// <param name="seed">The run's seed.</param>
    /// <param name="noLoot">
    /// True reproduces the loot-less 2026-08-14 series; false (the canonical form) rolls
    /// loot through <see cref="GauntletRun.CompleteFight"/>.
    /// </param>
    public static SeedResult RunSeed(
        SrdContent content,
        IReadOnlyList<LadderStep> ladder,
        int startLevel,
        int seed,
        bool noLoot)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(ladder);

        var run = GauntletRun.Start(content, ladder, startLevel, seed);
        var end = RunEnd.Cleared;
        var fights = new List<FightRecord>();
        var deaths = new List<DeathRecord>();

        while (run.Next is not null)
        {
            var step = run.Next!;

            // The one reseed point, matching both clients: everything from here through
            // this fight's loot draws from RunDice.SeedFor(run.Seed, run.Cleared) — see
            // RunDice's remarks. This measures the game as actually played, not a
            // continuous per-seed stream a player's process never runs.
            var random = new SeededRandomSource(RunDice.SeedFor(run.Seed, run.Cleared));
            var rest = run.PrepareForNext(random);

            // The Long Rest merchant is part of the game as played: the canonical run
            // spends its winnings the way the auto-buyer does, so the economy's effect on
            // pacing is measured rather than accrued and ignored.
            if (rest == RestKind.Long)
            {
                Shop.AutoBuy(content, run);
            }

            var fight = run.BeginNext(random);

            if (fight.Built.Monsters.Count == 0)
            {
                end = RunEnd.NoFight;
                break;
            }

            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);

            // The policy's round limit fired: the fight cannot resolve, so the run's
            // story ends here with whatever it had cleared. Counted separately from
            // defeat rather than folded into it: a party that died and a fight that
            // would not resolve look identical in "fights cleared" and mean opposite
            // things about a change. A battlefield the policy cannot cross shows up
            // here and nowhere else.
            if (!fight.Encounter.IsComplete)
            {
                end = RunEnd.Stalled;
                break;
            }

            var fightNumber = run.Cleared + 1;
            var thisStep = step;

            var heroes = fight.Encounter.Combatants
                .Where(c => c.SideId == PregeneratedParty.SideId)
                .ToArray();

            // Read at the same instant IsComplete went true — WinningSide is set
            // alongside it (Encounter.cs) — so "won" means this fight, not the run.
            var won = fight.Encounter.WinningSide == PregeneratedParty.SideId;

            fights.Add(new FightRecord(
                fightNumber,
                thisStep.Difficulty.ToString(),
                heroes.Sum(c => (double)c.CurrentHitPoints)
                    / Math.Max(1, heroes.Sum(c => c.Stats.MaximumHitPoints)),
                heroes.Count(c => c.CurrentHitPoints == 0 || c.IsDead),
                fight.Encounter.Round,
                fight.Built.Monsters.Count,
                won));

            run.CompleteFight(fight, noLoot ? null : random);

            if (run.Outcome != RunOutcome.InProgress)
            {
                end = run.Outcome == RunOutcome.Defeated ? RunEnd.Defeated : RunEnd.Cleared;

                if (end == RunEnd.Defeated)
                {
                    deaths.Add(new DeathRecord(
                        fightNumber,
                        thisStep.Difficulty.ToString(),
                        fight.Built.Monsters.Count,
                        fight.Built.Monsters.Max(BiggestHit)));
                }

                break;
            }
        }

        var level = run.Party.Max(member => member.Sheet.Level);

        return new SeedResult(run.Cleared, level, end, fights, deaths);
    }

    /// <summary>The largest printed average hit any of a monster's attacks can deal.</summary>
    public static int BiggestHit(MonsterDefinition monster) =>
        monster.Entries.SelectMany(e => e.Attack is null ? [] : new[] { e.Attack })
            .Select(a => a.Damage.Sum(d => d.PrintedAverage))
            .DefaultIfEmpty(0)
            .Max();
}
