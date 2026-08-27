namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="Chrome.Trim"/>: a pure function over a string and a width, reachable
/// from a headless test host with no Godot runtime at all — it was untested before
/// #327's S7 gave it a home outside <c>FightScreen</c>.
/// </summary>
public class ChromeTests
{
    [Fact]
    public void Trim_LeavesShortTextUntouched()
    {
        Assert.Equal("Goblin", Chrome.Trim("Goblin", 10));
    }

    [Fact]
    public void Trim_LeavesTextExactlyAtWidthUntouched()
    {
        Assert.Equal("Goblin", Chrome.Trim("Goblin", 6));
    }

    [Fact]
    public void Trim_CutsLongTextAndMarksItWithAnEllipsis()
    {
        Assert.Equal("Gobli…", Chrome.Trim("Goblin Warrior", 6));
    }

    [Fact]
    public void Trim_ResultNeverExceedsTheRequestedWidth()
    {
        Assert.Equal(6, Chrome.Trim("Goblin Warrior", 6).Length);
    }
}
