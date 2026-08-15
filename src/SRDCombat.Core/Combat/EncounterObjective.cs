namespace SRDCombat.Core.Combat;

/// <summary>What a side has to do to win a fight.</summary>
public enum ObjectiveKind
{
    /// <summary>Last side standing — the rule every fight used before objectives existed.</summary>
    Defeat,

    /// <summary>Hold out for a stated number of rounds.</summary>
    SurviveRounds,

    /// <summary>Kill one marked enemy; the rest break off.</summary>
    KillLeader,
}

/// <summary>
/// The condition that ends a fight in one side's favour.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a design decision, not a printed rule.</b> The SRD describes how creatures
/// fight, never what a fight is *for* — that is the Game Master's, exactly as the XP award
/// between two published tables is (see <c>ExperienceRules</c>). So the readings are
/// stated here rather than derived, and the type is deliberately small: an objective says
/// what ends the fight early and nothing else.
/// </para>
/// <para>
/// <b>Losing is not an objective.</b> Every objective belongs to one side and can only
/// ever *win* the fight for it. Being wiped out still loses, whatever the objective says,
/// because <c>Encounter.CheckForCompletion</c> settles the last-side-standing question
/// first and only then asks whether an objective was met. A side cannot meet an objective
/// it has no one left to meet.
/// </para>
/// <para>
/// <b>The other side's objective is always Defeat.</b> A "survive three rounds" fight is
/// a fight the monsters win by killing the party and lose by failing to, which is what
/// makes it a different fight rather than a shorter one — and it is why nothing here
/// carries an objective for the opposing side. If a monster side ever needs one of its
/// own, it belongs on this type rather than in a second mechanism.
/// </para>
/// <para>
/// <b>Rewards are unaffected, and that fell out rather than being decided.</b>
/// <c>GauntletRun</c> awards experience and gold from <c>fight.Built.Monsters</c> — the
/// encounter as built, not the corpses on the field — so a fight won by outlasting an
/// enemy that walks away pays exactly what killing it would have. That was already true
/// before objectives existed and is the behaviour objectives need, so no rule moved.
/// </para>
/// </remarks>
public sealed record EncounterObjective
{
    private EncounterObjective(ObjectiveKind kind, string? sideId, int rounds, string? leaderId)
    {
        Kind = kind;
        SideId = sideId;
        Rounds = rounds;
        LeaderId = leaderId;
    }

    /// <summary>Last side standing. The default, and what every fight was before this type.</summary>
    public static EncounterObjective Defeat { get; } = new(ObjectiveKind.Defeat, null, 0, null);

    /// <summary>What ends the fight.</summary>
    public ObjectiveKind Kind { get; }

    /// <summary>Whose objective it is. Null only for <see cref="Defeat"/>, which is nobody's.</summary>
    public string? SideId { get; }

    /// <summary>How many rounds must be survived, for <see cref="ObjectiveKind.SurviveRounds"/>.</summary>
    public int Rounds { get; }

    /// <summary>The combatant who must die, for <see cref="ObjectiveKind.KillLeader"/>.</summary>
    public string? LeaderId { get; }

    /// <summary>Hold out for <paramref name="rounds"/> rounds and the fight is won.</summary>
    /// <param name="sideId">The side that must survive.</param>
    /// <param name="rounds">
    /// Rounds to last. Counted inclusively — the fight ends the moment round
    /// <paramref name="rounds"/> has been played out in full, so 3 means rounds 1, 2 and 3
    /// happen and the fourth never begins.
    /// </param>
    public static EncounterObjective SurviveRounds(string sideId, int rounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sideId);
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);

        return new EncounterObjective(ObjectiveKind.SurviveRounds, sideId, rounds, null);
    }

    /// <summary>Kill the marked enemy and the fight is won, whatever else still stands.</summary>
    /// <param name="sideId">The side that must do the killing.</param>
    /// <param name="leaderId">The combatant whose death ends it.</param>
    public static EncounterObjective KillLeader(string sideId, string leaderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sideId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderId);

        return new EncounterObjective(ObjectiveKind.KillLeader, sideId, leaderId: leaderId, rounds: 0);
    }

    /// <summary>
    /// One line naming what the side has to do, for a client to show before the first turn.
    /// </summary>
    /// <remarks>
    /// Composed here rather than in either client, the same reason <c>TurnBanner</c> and
    /// <c>ShopOffer.Effect</c> are: two clients wording it separately would be two places
    /// for it to drift.
    /// </remarks>
    /// <param name="leaderName">
    /// The marked enemy's name, for <see cref="ObjectiveKind.KillLeader"/>. Falls back to
    /// "the leader" when a caller has no name to hand.
    /// </param>
    public string Describe(string? leaderName = null) => Kind switch
    {
        ObjectiveKind.SurviveRounds => Rounds == 1
            ? "Survive 1 round."
            : $"Survive {Rounds} rounds.",
        ObjectiveKind.KillLeader =>
            $"Kill {(string.IsNullOrWhiteSpace(leaderName) ? "the leader" : leaderName)} — the rest will break off.",
        _ => "Defeat every enemy.",
    };
}
