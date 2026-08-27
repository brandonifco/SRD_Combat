using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The focus stack: what has the player's attention, innermost last.
/// </summary>
/// <remarks>
/// Exercised through a local focus type rather than <c>PlayFocus</c>, because this
/// collection is deliberately screen-agnostic (#500) — the battle builder's shell (#482)
/// brings its own. A test that could only be written against <c>PlayFocus</c> would be
/// evidence the generic had grown an opinion it should not have.
/// </remarks>
public class FocusStackTests
{
    private abstract record Layer
    {
        internal sealed record Root : Layer;

        internal sealed record Menu(string Name) : Layer;

        internal sealed record Prompt : Layer;
    }

    private static FocusStack<Layer> Fresh() => new(new Layer.Root());

    [Fact]
    public void AFreshStackIsItsRoot()
    {
        var stack = Fresh();

        Assert.Equal(1, stack.Depth);
        Assert.Equal(stack.Root, stack.Top);
    }

    [Fact]
    public void PushPutsTheNewLayerOnTop()
    {
        var stack = Fresh();

        stack.Push(new Layer.Menu("attacks"));

        Assert.Equal(2, stack.Depth);
        Assert.Equal(new Layer.Menu("attacks"), stack.Top);
        Assert.Equal(new Layer.Root(), stack.Root);
    }

    [Fact]
    public void PopReturnsTheLayerItClosedAndUncoversTheOneBelow()
    {
        var stack = Fresh();
        stack.Push(new Layer.Menu("spells"));
        stack.Push(new Layer.Prompt());

        Assert.Equal(new Layer.Prompt(), stack.Pop());
        Assert.Equal(new Layer.Menu("spells"), stack.Top);
        Assert.Equal(2, stack.Depth);
    }

    /// <summary>
    /// The invariant the type exists for: a screen always has something focused. The seven
    /// booleans this replaces could all be false at once — a state the screen had no name
    /// for and no drawing of.
    /// </summary>
    [Fact]
    public void PopRefusesTheRoot()
    {
        var stack = Fresh();

        Assert.Null(stack.Pop());
        Assert.Equal(1, stack.Depth);
        Assert.Equal(new Layer.Root(), stack.Top);
    }

    [Fact]
    public void PopToRootClosesEveryLayerAbove()
    {
        var stack = Fresh();
        stack.Push(new Layer.Menu("spells"));
        stack.Push(new Layer.Menu("slots"));
        stack.Push(new Layer.Prompt());
        Assert.Equal(4, stack.Depth);

        stack.PopToRoot();

        Assert.Equal(1, stack.Depth);
        Assert.Equal(new Layer.Root(), stack.Top);
    }

    [Fact]
    public void PopToRootOnAStackAlreadyAtItsRootChangesNothing()
    {
        var stack = Fresh();

        stack.PopToRoot();

        Assert.Equal(1, stack.Depth);
    }

    [Fact]
    public void ReplaceTopSwapsWithoutChangingDepth()
    {
        var stack = Fresh();
        stack.Push(new Layer.Menu("spells"));

        stack.ReplaceTop(new Layer.Menu("slots"));

        Assert.Equal(2, stack.Depth);
        Assert.Equal(new Layer.Menu("slots"), stack.Top);
    }

    [Fact]
    public void HoldsFindsALayerThatIsNotOnTop()
    {
        var stack = Fresh();
        stack.Push(new Layer.Menu("spells"));
        stack.Push(new Layer.Prompt());

        Assert.True(stack.Holds<Layer.Menu>());
        Assert.True(stack.Holds<Layer.Prompt>());
    }

    [Fact]
    public void HoldsIsFalseForAKindNobodyPushed()
    {
        var stack = Fresh();

        Assert.False(stack.Holds<Layer.Menu>());
    }

    [Fact]
    public void TopmostFindsTheHighestOfSeveral()
    {
        var stack = Fresh();
        stack.Push(new Layer.Menu("spells"));
        stack.Push(new Layer.Menu("slots"));

        Assert.Equal(new Layer.Menu("slots"), stack.Topmost<Layer.Menu>());
    }

    [Fact]
    public void TopmostIsNullForAKindNobodyPushed()
    {
        Assert.Null(Fresh().Topmost<Layer.Menu>());
    }

    [Fact]
    public void BottomUpReadsRootFirst()
    {
        var stack = Fresh();
        stack.Push(new Layer.Menu("spells"));
        stack.Push(new Layer.Prompt());

        Assert.Equal(
            [new Layer.Root(), new Layer.Menu("spells"), (Layer)new Layer.Prompt()],
            stack.BottomUp);
    }
}
