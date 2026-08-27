namespace SRDCombat.Viewer.Ui;

/// <summary>
/// What has the player's attention, innermost last. One layer per thing that can be
/// open at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>A collection, not a framework</b> (#500, <c>docs/2026-08-26-playmode-refactor-design.md</c>
/// §3). There is deliberately no interface, no registry and no per-screen abstraction
/// here: a screen declares its own focus type and reuses this. The battle builder's
/// shell (#482) is the second caller this shape is sized for, and it will bring its own
/// <c>TFocus</c> rather than implementing anything of this file's.
/// </para>
/// <para>
/// <b>The root never leaves.</b> A screen always has something focused — for
/// <c>PlayMode</c> that is the board — so <see cref="Pop"/> refuses to empty the stack
/// rather than returning an empty state nobody downstream could draw. That refusal is
/// the invariant this type exists to hold; the seven booleans it replaces could each be
/// false at once, which is a state the screen had no name for and no drawing of.
/// </para>
/// </remarks>
/// <typeparam name="TFocus">The screen's own focus type.</typeparam>
internal sealed class FocusStack<TFocus>
    where TFocus : notnull
{
    private readonly List<TFocus> _layers;

    /// <summary>Starts a stack whose root is <paramref name="root"/>.</summary>
    internal FocusStack(TFocus root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _layers = [root];
    }

    /// <summary>The bottom layer, which is always present and never popped.</summary>
    internal TFocus Root => _layers[0];

    /// <summary>The layer with the player's attention.</summary>
    internal TFocus Top => _layers[^1];

    /// <summary>How many layers are open, counting the root. Never less than one.</summary>
    internal int Depth => _layers.Count;

    /// <summary>Every layer, root first — the order a screen draws them in.</summary>
    internal IReadOnlyList<TFocus> BottomUp => _layers;

    /// <summary>Opens a layer over the current one.</summary>
    internal void Push(TFocus focus)
    {
        ArgumentNullException.ThrowIfNull(focus);
        _layers.Add(focus);
    }

    /// <summary>
    /// Closes the top layer and returns it, or returns default when only the root is
    /// left — the root is not popped.
    /// </summary>
    internal TFocus? Pop()
    {
        if (_layers.Count == 1)
        {
            return default;
        }

        var top = _layers[^1];
        _layers.RemoveAt(_layers.Count - 1);
        return top;
    }

    /// <summary>Closes every layer above the root.</summary>
    internal void PopToRoot()
    {
        if (_layers.Count > 1)
        {
            _layers.RemoveRange(1, _layers.Count - 1);
        }
    }

    /// <summary>
    /// Swaps the top layer for another. Replacing the root is legal — a screen whose
    /// root itself changes is replacing what it is, not opening something over it.
    /// </summary>
    internal void ReplaceTop(TFocus focus)
    {
        ArgumentNullException.ThrowIfNull(focus);
        _layers[^1] = focus;
    }

    /// <summary>Whether any open layer is of this kind.</summary>
    internal bool Holds<T>()
        where T : TFocus =>
        _layers.OfType<T>().Any();

    /// <summary>The topmost layer of this kind, or null when none is open.</summary>
    internal T? Topmost<T>()
        where T : class, TFocus
    {
        for (var index = _layers.Count - 1; index >= 0; index--)
        {
            if (_layers[index] is T match)
            {
                return match;
            }
        }

        return null;
    }
}
