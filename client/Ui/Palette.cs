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

    /// <summary>The translucent wash the overlays share, so the field reads underneath.</summary>
    internal static readonly Color Veil = new(Background.R, Background.G, Background.B, 0.85f);
}
