using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Timed condition durations: "for 1 minute" as ten of the bearer's turns, and
/// "for 1 hour" as a duration no fight reaches.
/// </summary>
/// <remarks>
/// Both are stated interpretations recorded on <see cref="ConditionDuration"/>: a minute
/// is ten rounds ending at the end of the bearer's tenth turn from application, and an
/// hour or longer means the condition ends only with the encounter. The expiry machinery
/// is the same turn counter every "next turn" duration already rides — a timed duration
/// is just the clock set further out.
/// </remarks>
public class TimedDurationTests
{
    [Fact]
    public void ForOneMinuteExpiresAtTheEndOfTheBearersTenthTurn()
    {
        var source = CombatTestData.Combatant("source");
        var bearer = CombatTestData.Combatant("bearer", x: 1);

        var expiry = ConditionRules.ExpiryFor(ConditionDuration.ForMinutes(1), source, bearer);

        Assert.Equal(new ConditionExpiry("bearer", ConditionClock.EndOfTurn, bearer.TurnsBegun + 10), expiry);
    }

    [Fact]
    public void BeyondTheFightHasNoExpiry()
    {
        // "for 1 hour": the rider is imposable and the condition simply never expires —
        // exactly like one printed with no duration at all, while the printed shape
        // stays recorded on the duration for narration and accounting.
        var source = CombatTestData.Combatant("source");
        var bearer = CombatTestData.Combatant("bearer", x: 1);

        Assert.Null(ConditionRules.ExpiryFor(ConditionDuration.BeyondTheFight, source, bearer));
    }

    [Fact]
    public void ATimedConditionOutlivesEarlierTurnsAndEndsOnItsOwn()
    {
        // A two-combatant fight, and a Poisoned that ends at the end of the bearer's
        // second turn from now — the same arithmetic "for 1 minute" uses, kept short so
        // the test walks it. It must survive the bearer's next end of turn and end at
        // the one after.
        var bearer = CombatTestData.Combatant("bearer", stats: CombatTestData.Stats(initiativeBonus: 10));
        var other = CombatTestData.Combatant("other", sideId: CombatTestData.Monsters, x: 5);
        var encounter = Encounter.Start(new Battlefield(12, 12), [bearer, other], new ScriptedRandomSource(20, 1));

        // Applied during the bearer's own first turn: + 2 counts two turns beyond the
        // current one, exactly as + 1 is "next turn" — so the clock runs out at the end
        // of the bearer's third turn.
        bearer.AddCondition(new ActiveCondition(
            ConditionType.Poisoned,
            "other",
            new ConditionExpiry("bearer", ConditionClock.EndOfTurn, bearer.TurnsBegun + 2)));

        encounter.EndTurn(); // bearer's first end of turn
        Assert.True(bearer.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn(); // other's turn — not the bearer's clock
        encounter.EndTurn(); // bearer's second end of turn — one more to go
        Assert.True(bearer.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn(); // other again
        encounter.EndTurn(); // bearer's third end of turn — the clock runs out
        Assert.False(bearer.HasCondition(ConditionType.Poisoned));
    }
}
