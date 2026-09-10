using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// The client's one set of named colours. Every screen refers to these rather than
/// declaring its own — before this type existed, <c>CreateMode</c> inherited them from
/// <c>FightScreen</c> for no reason but palette reuse, which is the shape #327's S7
/// replaced with composition. Moved verbatim; the values themselves do not change.
/// </summary>
internal static class Palette
{
    internal static readonly Color Background = new("16161d");
    internal static readonly Color GridLine = new("2c2c38");
    internal static readonly Color Difficult = new("2a2438");

    /// <summary>
    /// What difficult ground looks like over real terrain: dark enough to read as rough
    /// going, sheer enough to leave the tile underneath recognisable.
    /// </summary>
    internal static readonly Color DifficultWash = new(0.10f, 0.06f, 0.16f, 0.45f);
    internal static readonly Color Blocked = new("3a2a2a");
    internal static readonly Color LowObstacle = new("4a4032");
    internal static readonly Color PartyColour = new("5a9fd4");
    internal static readonly Color MonsterColour = new("c4614f");
    internal static readonly Color DeadColour = new("4a4a52");
    internal static readonly Color DownColour = new("8a6a4a");
    internal static readonly Color ActiveRing = new("e8d5a0");
    internal static readonly Color Ink = new("d8d8e0");
    internal static readonly Color Dim = new("8a8a96");

    /// <summary>
    /// The health readout's "fine" colour (#299) — more than half its hit points, the
    /// side of the SRD's own printed Bloodied threshold (glossary p. 177) that carries
    /// no warning. A green found nowhere else in this file, so a standing hit-point bar
    /// or hp line reading healthy is its own colour rather than borrowing a team's.
    /// </summary>
    internal static readonly Color HealthyColour = new("5ab06a");

    /// <summary>
    /// The health readout's threshold colour (#299) — "half its Hit Points or fewer",
    /// the SRD's own printed Bloodied condition (glossary p. 177, quoted on
    /// <c>HealthBand</c>). Distinct from <see cref="MonsterColour"/> and
    /// <see cref="ThreatMark"/> so a Bloodied ally never reads as an enemy's own
    /// identity colour or a targeting warning.
    /// </summary>
    internal static readonly Color BloodiedColour = new("c9433f");

    /// <summary>A filled Death Saving Throw success pip (#299) — <see cref="HealthyColour"/>'s family; good news, drawn small.</summary>
    internal static readonly Color DeathSaveSuccessColour = new("6fcf8f");

    /// <summary>A filled Death Saving Throw failure pip (#299) — <see cref="BloodiedColour"/>'s family, one shade darker so three filled pips read as "close" rather than as loud as the death they are one step from.</summary>
    internal static readonly Color DeathSaveFailureColour = new("a83232");

    /// <summary>
    /// A downed character who has stopped rolling Death Saves (#299,
    /// <c>Combatant.MarkStable</c>) — its own hue rather than <see cref="HealthyColour"/>,
    /// since Stable is not "fine": the creature is still at 0 hit points and still
    /// Unconscious, only no longer one bad roll from dying.
    /// </summary>
    internal static readonly Color StableColour = new("6a8caf");

    /// <summary>
    /// The hovered move's route (#303) — <c>ActiveRing</c>'s own hue, since a path
    /// preview is the same "here is what your attention is on" signal the turn cursor
    /// already draws in, at higher opacity than <c>PartyColour</c>'s reachable wash
    /// (0.16) so the one square out of many that the pointer means to walk to reads as
    /// singled out rather than merely part of the crowd.
    /// </summary>
    internal static readonly Color PathPreview = new(ActiveRing.R, ActiveRing.G, ActiveRing.B, 0.35f);

    /// <summary>
    /// A step on the previewed route whose crossing would provoke an Opportunity
    /// Attack (#301) — a fully-opaque, saturated warning hue, drawn as a border rather
    /// than another translucent wash. The review that opened #301 named the two
    /// existing advisory layers as "the least visible things on screen (0.16-alpha
    /// movement wash; red ring on a red token)"; a third barely-there wash on top of
    /// those would repeat the complaint rather than answer it. Distinct from
    /// <c>MonsterColour</c>'s muted red-orange (a token's own colour) so a threat mark
    /// never reads as another enemy standing in the square.
    /// </summary>
    internal static readonly Color ThreatMark = new("ff5a3c");

    /// <summary>The translucent wash the overlays share, so the field reads underneath.</summary>
    internal static readonly Color Veil = new(Background.R, Background.G, Background.B, 0.85f);

    /// <summary>
    /// The normal-range band of an armed attack's or spell's envelope (#302) — every
    /// square a target could stand in for the click that is about to happen to
    /// actually reach, queried from the engine's own <c>CombatAttack.CanReach</c> /
    /// <c>SpellDefinition.TargetRangeFeet</c>, never re-derived. A cool teal, distinct
    /// from <see cref="PathPreview"/>'s amber (a route) and <see cref="ThreatMark"/>'s
    /// warm red (a warning), so "you may aim here" reads as its own kind of advice.
    /// </summary>
    internal static readonly Color RangeNormal = new(0.30f, 0.74f, 0.66f, 0.22f);

    /// <summary>
    /// The long-range band of an armed ranged attack (#302) — beyond
    /// <c>CombatAttack</c>'s own printed normal range but still reachable at
    /// Disadvantage (<c>CombatAttack.IsAtLongRange</c>). <see cref="RangeNormal"/>'s own
    /// hue at roughly half the opacity, so the two read as one continuous envelope with
    /// a fainter far edge rather than two unrelated colours.
    /// </summary>
    internal static readonly Color RangeLong = new(RangeNormal.R, RangeNormal.G, RangeNormal.B, 0.11f);

    /// <summary>
    /// Exactly what an armed area spell would cover for the hovered origin (#302) —
    /// <c>AreaTargeting.Cover</c>'s own answer, the identical call
    /// <c>Encounter.CastSpell</c> makes to resolve the real cast. A violet — the same
    /// family <c>LogHighlighter.ActionName</c> colours a spell's own name — so the wash
    /// reads as "this spell" rather than competing with <see cref="RangeNormal"/>'s
    /// cooler, weapon-flavoured advice.
    /// </summary>
    internal static readonly Color AreaCoverage = new(0.68f, 0.55f, 0.86f, 0.30f);

    /// <summary>
    /// A creature an armed area spell's coverage would actually catch (#302) — <see
    /// cref="AreaCoverage"/>'s own hue at full opacity, drawn as a ring the same way the
    /// plain "can this attack reach them" ring already is. Distinct from <see
    /// cref="ThreatMark"/> and <see cref="MonsterColour"/> so a caught ally is never
    /// misread as a threat.
    /// </summary>
    internal static readonly Color AreaCaught = new("b48ee0");

    /// <summary>
    /// The square a hovered target sits on when it is outside an armed attack's or
    /// spell's own range (#302) — shown before the click, for the same refusal
    /// (<c>attack.out_of_range</c>, <c>client.no_attack</c>, <c>spell.out_of_range</c>)
    /// the click would produce after it. A muted, desaturated red: a warning, but
    /// deliberately less saturated than <see cref="ThreatMark"/>'s so the two are never
    /// confused — a threatened step is a cost a walk would pay, this is a target the
    /// click could not reach at all.
    /// </summary>
    internal static readonly Color OutOfRange = new("a04a52");
}
