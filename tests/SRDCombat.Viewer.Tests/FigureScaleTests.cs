using SRDCombat.Core.Definitions;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// How big a figure is drawn: the shared pixel scale, the quarter-step snap, and the
/// footprint clamp.
/// </summary>
/// <remarks>
/// <para>
/// <b>The silent one.</b> A figure at the wrong scale still renders — it is simply the
/// wrong size, or its source pixels come out uneven and crawl as the frames cycle. #296
/// is the case in point: the clamp inverted the printed size ordering and shipped, with
/// an Ogre rendering smaller than a Goblin, and nothing failed.
/// </para>
/// <para>
/// <c>FightScreen</c> derives from a Godot node and cannot be constructed here, but a
/// static method on it can be called: the type loads and its static fields (all
/// <c>Color</c>, a managed struct) initialise outside the engine. So the rule takes the
/// square's pixel size as an argument rather than reading it off the screen (#190), and
/// nothing else about it moved.
/// </para>
/// </remarks>
public class FigureScaleTests
{
    /// <summary>
    /// A square is 66 pixels at today's board and a standing human is drawn 64 tall, so
    /// a Medium creature lands on exactly 1.0 — the art at its own resolution. This is
    /// the claim <c>ScaleFor</c>'s own doc comment makes.
    /// </summary>
    [Fact]
    public void ScaleFor_DrawsAMediumCreatureAtItsOwnResolutionOnTodaysBoard()
    {
        var scale = FightScreen.ScaleFor(Figure(stature: 64, breadth: 40), CreatureSize.Medium, 66f);

        Assert.Equal(1f, scale);
    }

    /// <summary>
    /// Pixel art enlarged by an arbitrary fraction gives its source pixels uneven sizes
    /// on screen, which crawls as the frames cycle. Every scale is a clean quarter step.
    /// </summary>
    [Theory]
    [InlineData(40f)]
    [InlineData(48f)]
    [InlineData(53f)]
    [InlineData(66f)]
    [InlineData(80f)]
    public void ScaleFor_SnapsToAQuarterStep(float cellPixels)
    {
        foreach (var size in Enum.GetValues<CreatureSize>())
        {
            var scale = FightScreen.ScaleFor(Figure(stature: 64, breadth: 40), size, cellPixels);

            Assert.Equal(scale * 4f, MathF.Round(scale * 4f));
        }
    }

    /// <summary>
    /// A bigger printed size is drawn bigger, art held equal. This is the target scale
    /// the #296 fix put a floor under, and it is what the footprint clamp used to be
    /// able to override.
    /// </summary>
    [Fact]
    public void ScaleFor_DrawsALargerPrintedSizeLarger()
    {
        var art = Figure(stature: 64, breadth: 40);

        var medium = FightScreen.ScaleFor(art, CreatureSize.Medium, 66f);
        var large = FightScreen.ScaleFor(art, CreatureSize.Large, 66f);

        Assert.True(large > medium, $"Large drew at {large}, Medium at {medium}");
    }

    /// <summary>
    /// <b>#296's bug, pinned.</b> The Ogre and the Ettin are Large and drawn with a
    /// raised club and a second head spanning nearly the whole canvas, so their measured
    /// footprint trips the clamp that a narrow Small goblin never touches — and the clamp
    /// then decided size, inverting the printed ordering. A bigger creature's stance
    /// genuinely needs more room, so the allowance grows with printed size too.
    /// </summary>
    [Fact]
    public void ScaleFor_KeepsALargeCreatureBiggerThanASmallOneEvenWhenItsArtIsWide()
    {
        var ogre = Figure(stature: 60, breadth: 150);
        var goblin = Figure(stature: 44, breadth: 26);

        var large = FightScreen.ScaleFor(ogre, CreatureSize.Large, 66f);
        var small = FightScreen.ScaleFor(goblin, CreatureSize.Small, 66f);

        Assert.True(large > small, $"Large drew at {large}, Small at {small}");
    }

    /// <summary>
    /// The clamp is still a real clamp: art disproportionate even by its own size's
    /// generous allowance is cut down rather than let out of its square.
    /// </summary>
    [Fact]
    public void ScaleFor_StillShrinksAFigureFarWiderThanItsSquareAllows()
    {
        var sprawling = Figure(stature: 64, breadth: 400);

        var scale = FightScreen.ScaleFor(sprawling, CreatureSize.Medium, 66f);

        Assert.True(scale < 1f, $"a 400px-wide Medium figure should be cut down, got {scale}");
    }

    /// <summary>
    /// Snapped <em>down</em> for an oversized figure: rounding to the nearest quarter
    /// could round back up past the very bound being applied.
    /// </summary>
    [Fact]
    public void ScaleFor_SnapsDownRatherThanNearestWhenTheClampApplies()
    {
        // Breadth chosen so the clamp's own bound sits just above a quarter step:
        // 1.6 * 1 * 66 / 141 = 0.7489, which rounds up to 0.75 and floors to 0.5.
        var scale = FightScreen.ScaleFor(Figure(stature: 64, breadth: 141), CreatureSize.Medium, 66f);

        Assert.Equal(0.5f, scale);
    }

    [Fact]
    public void ScaleFor_NeverDrawsBelowAQuarter()
    {
        var scale = FightScreen.ScaleFor(Figure(stature: 4000, breadth: 4000), CreatureSize.Tiny, 66f);

        Assert.Equal(0.25f, scale);
    }

    private static SpriteLibrary.Figure Figure(int stature, int breadth) =>
        new(CentreX: 32f, GroundY: 64f, stature, breadth);
}
