using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Rules;

/// <summary>The two rests the SRD defines.</summary>
public enum RestKind
{
    /// <summary>"A Short Rest is a 1-hour period of downtime."</summary>
    Short,

    /// <summary>"A Long Rest is a period of extended downtime—at least 8 hours."</summary>
    Long,
}

/// <summary>
/// What a Short Rest and a Long Rest restore.
/// </summary>
/// <remarks>
/// <para>
/// Read off the printed glossary rather than remembered, and it corrected memory once:
/// <b>a Long Rest restores <em>all</em> spent Hit Point Dice</b> — "You regain all lost
/// Hit Points and all spent Hit Point Dice" — where earlier editions returned half.
/// </para>
/// <para>
/// <b>The features differ from each other, which is why this is a table and not a
/// reset.</b> Rage and Second Wind both print "You regain one expended use when you
/// finish a Short Rest, and you regain all expended uses when you finish a Long Rest".
/// Action Surge prints "you can't do so again until you finish a Short or Long Rest",
/// so it comes back whole on either. Spell slots return on a Long Rest only. Treating
/// them alike would hand a Fighter unlimited Action Surges or take a Barbarian's
/// short-rest Rage away.
/// </para>
/// <para>
/// <b>Both rests require a hit point to start</b> — "To start a Short Rest, you must
/// have at least 1 Hit Point", and the same for a Long Rest. A character at 0 cannot
/// rest their way back, which is a real constraint on a party between fights rather
/// than an oversight.
/// </para>
/// </remarks>
public static class RestRules
{
    /// <summary>True when a creature at this many hit points may begin a rest.</summary>
    public static bool CanRest(int currentHitPoints) => currentHitPoints >= 1;

    /// <summary>
    /// Uses left of a feature that returns one on a Short Rest and all on a Long Rest —
    /// Rage and Second Wind.
    /// </summary>
    public static int OneOnShortAllOnLong(RestKind rest, int remaining, int maximum) => rest switch
    {
        RestKind.Short => Math.Min(maximum, remaining + 1),
        RestKind.Long => maximum,
        _ => remaining,
    };

    /// <summary>
    /// Uses left of a feature that returns whole on either rest — Action Surge.
    /// </summary>
    public static int AllOnEitherRest(RestKind rest, int maximum) => maximum;

    /// <summary>Hit points after a Long Rest: "You regain all lost Hit Points".</summary>
    public static int HitPointsAfterLongRest(int maximum) => maximum;

    /// <summary>
    /// Hit Point Dice left after a rest. A Long Rest returns every one; a Short Rest
    /// returns none, being where they are spent rather than regained.
    /// </summary>
    public static int HitDiceAfter(RestKind rest, int remaining, int maximum) =>
        rest == RestKind.Long ? maximum : remaining;

    /// <summary>
    /// Spends one Hit Point Die on a Short Rest: "roll the die and add your Constitution
    /// modifier to it. You regain Hit Points equal to the total (minimum of 1 Hit Point)."
    /// </summary>
    /// <remarks>
    /// The minimum is per die rather than per rest, so a Constitution penalty can never
    /// make a spent die worth nothing.
    /// </remarks>
    public static int SpendHitDie(IRandomSource random, int hitDieSides, int constitutionModifier)
    {
        ArgumentNullException.ThrowIfNull(random);

        return Math.Max(1, random.Roll(hitDieSides) + constitutionModifier);
    }

    /// <summary>
    /// Hit points a Stable creature has recovered by the time the party moves on.
    /// </summary>
    /// <remarks>
    /// "A Stable creature that isn't healed regains 1 Hit Point after 1d4 hours." This
    /// game has no clock, so the reading is stated rather than derived: <b>the interval
    /// between two fights in the gauntlet is taken to be at least four hours</b>, which
    /// is the longest that roll can take, so a character who went down and survived is
    /// conscious at 1 hit point when the next fight begins. Without that they could
    /// never rest either, since a rest needs a hit point to start, and a party that lost
    /// one member would be stuck.
    /// </remarks>
    public const int HitPointsAfterStabilising = 1;
}
