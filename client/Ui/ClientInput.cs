namespace SRDCombat.Viewer.Ui;

/// <summary>What kind of input arrived.</summary>
internal enum ClientInputKind
{
    /// <summary>A key going down. Key repeats included — the screen wants them.</summary>
    KeyPressed,

    /// <summary>A mouse button going down.</summary>
    MousePressed,

    /// <summary>The pointer moved.</summary>
    MouseMoved,

    /// <summary>
    /// Anything else the engine reports — a key or button going up, a joypad, a gesture.
    /// </summary>
    /// <remarks>
    /// Carried rather than dropped because the quit confirmation swallows <em>every</em>
    /// event while it is up, not just the ones with meaning. Translating these to null
    /// would let a release or a drag reach the camera from behind the card, which is a
    /// behaviour change the refactor has no business making.
    /// </remarks>
    Other,
}

/// <summary>
/// The keys the router decides anything about, plus <see cref="Other"/> for everything
/// it only passes through as a character.
/// </summary>
internal enum ClientKey
{
    /// <summary>Any key the router has no rule of its own for.</summary>
    Other,

    Escape,
    Tab,
    Enter,
    Left,
    Right,
    Up,
    Down,
    Space,
}

/// <summary>
/// One input event, in the client's own vocabulary rather than Godot's.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <c>Godot.InputEvent</c>, and this is not a style preference.</b> Measured by
/// #190's architect: <c>Color</c>, <c>Vector2</c>, <c>Rect2</c> and the Godot enums are
/// ordinary managed structs and are safe in a test, but anything deriving from
/// <c>GodotObject</c>/<c>RefCounted</c> — <c>InputEventKey</c> and
/// <c>InputEventMouseButton</c> among them — <b>terminates the test host process when
/// constructed</b>. Not an exception that a test could assert on: an un-catchable abort
/// that fails every other test in the assembly with a misleading message. A router
/// taking <c>InputEvent</c> would be untestable, and would take #190's whole suite down
/// with it.
/// </para>
/// <para>
/// So the node translates at the boundary and the router decides in plain C#. That is
/// also what makes acceptance criterion 4 — one test per route branch — writable at all.
/// </para>
/// </remarks>
/// <param name="Kind">Key, mouse button, or motion.</param>
/// <param name="Key">Which key, for the ones the router rules on.</param>
/// <param name="Character">
/// The character a key press stands for, for hotkey lookup. <c>'\0'</c> when the event is
/// not a key or carries no character.
/// </param>
/// <param name="X">Pointer x, in screen pixels.</param>
/// <param name="Y">Pointer y, in screen pixels.</param>
internal readonly record struct ClientInput(
    ClientInputKind Kind,
    ClientKey Key,
    char Character,
    float X,
    float Y)
{
    /// <summary>A key press carrying no character — an arrow, Tab, Esc.</summary>
    internal static ClientInput Pressed(ClientKey key) =>
        new(ClientInputKind.KeyPressed, key, '\0', 0, 0);

    /// <summary>A key press standing for a character, for the action hotkeys.</summary>
    internal static ClientInput Typed(char character) =>
        new(ClientInputKind.KeyPressed, ClientKey.Other, character, 0, 0);

    /// <summary>A mouse button going down at a point.</summary>
    internal static ClientInput Clicked(float x, float y) =>
        new(ClientInputKind.MousePressed, ClientKey.Other, '\0', x, y);

    /// <summary>Whether this is a key going down, of any key.</summary>
    internal bool IsKey => Kind == ClientInputKind.KeyPressed;

    /// <summary>The step an arrow key means, or null for every other input.</summary>
    internal (int X, int Y)? ArrowStep => Key switch
    {
        ClientKey.Left => (-1, 0),
        ClientKey.Right => (1, 0),
        ClientKey.Up => (0, -1),
        ClientKey.Down => (0, 1),
        _ => null,
    };
}
