using Godot;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The sprite metrics: where a figure stands in its canvas, how big it is, and which
/// frame of a death strip is the corpse.
/// </summary>
/// <remarks>
/// <para>
/// Every rule here was a shipped bug once (CLAUDE.md names all three), and none of them
/// fails loudly — a figure measured wrong still renders, just at the wrong size or in
/// the wrong place. That is the silent-regression shape these tests exist for.
/// </para>
/// <para>
/// The sheets are built here as raw RGBA rather than as PNGs on disk, because decoding
/// a PNG is <c>Godot.Image</c> and <c>Godot.Image</c> is a native object: constructing
/// one outside a running engine does not throw, it takes the test host down with it.
/// So the measurement helpers were split into a reading half and a rule half (#190), and
/// this exercises the rule half.
/// </para>
/// </remarks>
public class SpriteMeasurementTests
{
    /// <summary>
    /// A hand-drawn set ships one drawing, not a strip. It is padded to a square frame
    /// with the drawing centred, and the box has to be reported in the padded frame's
    /// coordinates or the figure's centre line lands off to one side (#296).
    /// </summary>
    [Fact]
    public void FrameBoxes_ReportASingleNarrowDrawingWhereThePaddingPutsIt()
    {
        // 4 wide in an 8-tall canvas: padded to 8 wide, so the drawing starts at x = 2.
        var sheet = Sheet(width: 4, height: 8, opaque: (_, _) => true);

        var box = Assert.Single(SpriteLibrary.FrameBoxes(sheet, width: 4, frameSize: 8));

        Assert.Equal(new Rect2I(2, 0, 4, 8), box);
    }

    [Fact]
    public void FrameBoxes_MeasureEachFrameInItsOwnCoordinates()
    {
        // Two 4-frames side by side; the second frame's mark sits at its own x = 1.
        var sheet = Sheet(width: 8, height: 4, opaque: (x, y) => (x == 0 || x == 5) && y == 1);

        var boxes = SpriteLibrary.FrameBoxes(sheet, width: 8, frameSize: 4);

        Assert.Equal([new Rect2I(0, 1, 1, 1), new Rect2I(1, 1, 1, 1)], boxes);
    }

    /// <summary>
    /// A blank frame between poses is a legitimate part of a strip. It must contribute
    /// nothing rather than a zero-sized box, which would drag the median down.
    /// </summary>
    [Fact]
    public void FrameBoxes_SkipAnEmptyFrame()
    {
        var sheet = Sheet(width: 8, height: 4, opaque: (x, _) => x < 4);

        Assert.Single(SpriteLibrary.FrameBoxes(sheet, width: 8, frameSize: 4));
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    public void FrameBoxes_TreatNearlyTransparentPixelsAsAbsent(byte alpha, bool measured)
    {
        var sheet = Sheet(width: 4, height: 4, opaque: (_, _) => true, alpha: alpha);

        Assert.Equal(measured ? 1 : 0, SpriteLibrary.FrameBoxes(sheet, width: 4, frameSize: 4).Count);
    }

    /// <summary>
    /// The Wild Zombie kneels to feed and walks on all fours: measured on its idle it
    /// would be drawn at half the size it walks at, so stature comes from whichever
    /// standing strip is taller.
    /// </summary>
    [Fact]
    public void Measure_TakesStatureFromTheWalkStripWhenTheIdleCrouches()
    {
        var crouching = new[] { new Rect2I(0, 12, 8, 4) };
        var walking = new[] { new Rect2I(0, 4, 8, 12) };

        var figure = SpriteLibrary.Measure(crouching, walking, frameSize: 16);

        Assert.Equal(12, figure.Stature);
    }

    [Fact]
    public void Measure_KeepsTheIdlesStatureWhenTheWalkIsNoTaller()
    {
        var standing = new[] { new Rect2I(0, 4, 8, 12) };
        var walking = new[] { new Rect2I(0, 6, 8, 10) };

        var figure = SpriteLibrary.Measure(standing, walking, frameSize: 16);

        Assert.Equal(12, figure.Stature);
    }

    /// <summary>
    /// One lunging or crouching frame must not size the whole character — each strip is
    /// summarised by its median frame.
    /// </summary>
    [Fact]
    public void Measure_SummarisesAStripByItsMedianFrameNotItsExtreme()
    {
        var idle = new[]
        {
            new Rect2I(0, 4, 8, 12),
            new Rect2I(0, 4, 8, 12),
            new Rect2I(0, 15, 40, 1),
        };

        var figure = SpriteLibrary.Measure(idle, [], frameSize: 16);

        Assert.Equal(12, figure.Stature);
        Assert.Equal(8, figure.Breadth);
    }

    /// <summary>
    /// The packs do not agree on where the figure stands in its canvas — the Knight at
    /// 33 of its 128, the Goblin at 62 — so the centre line is measured, never assumed.
    /// </summary>
    [Fact]
    public void Measure_TakesTheCentreLineFromTheArtRatherThanTheCanvas()
    {
        var idle = new[] { new Rect2I(2, 4, 8, 12) };

        var figure = SpriteLibrary.Measure(idle, [], frameSize: 32);

        Assert.Equal(6f, figure.CentreX);
    }

    /// <summary>
    /// A pack that padded its canvas above the figure's feet would otherwise have its
    /// ground line float; the line never rises above three quarters of the canvas.
    /// </summary>
    [Fact]
    public void Measure_KeepsTheGroundLineNearTheCanvasFloor()
    {
        var floating = new[] { new Rect2I(0, 0, 8, 8) };

        var figure = SpriteLibrary.Measure(floating, [], frameSize: 32);

        Assert.Equal(24f, figure.GroundY);
    }

    /// <summary>
    /// <b>The named bug.</b> Every pack's death animation ends by taking the body away,
    /// so the last frame is a remnant and holding it made a killed goblin a red smear.
    /// The body settles on the fullest frame in which it is actually down.
    /// </summary>
    [Fact]
    public void RestingFrame_IsNotTheLastFrameOfTheStrip()
    {
        // Standing 24 tall: frames 2 and 3 are down (height under 12), and frame 4 is
        // the fade the strip ends on.
        var frames = new[]
        {
            (Index: 0, Height: 24, Pixels: 900),
            (Index: 1, Height: 18, Pixels: 800),
            (Index: 2, Height: 8, Pixels: 700),
            (Index: 3, Height: 8, Pixels: 400),
            (Index: 4, Height: 2, Pixels: 30),
        };

        var resting = SpriteLibrary.RestingFrame(frames, Standing(stature: 24));

        Assert.Equal(2, resting);
    }

    /// <summary>
    /// The Knights lie propped on an elbow and never drop below half their standing
    /// height; the fullest frame of the strip's second half skips the fade the same way.
    /// </summary>
    [Fact]
    public void RestingFrame_FallsBackToTheFullestFrameOfTheSecondHalf()
    {
        var frames = new[]
        {
            (Index: 0, Height: 24, Pixels: 900),
            (Index: 1, Height: 22, Pixels: 880),
            (Index: 2, Height: 20, Pixels: 850),
            (Index: 3, Height: 20, Pixels: 600),
        };

        var resting = SpriteLibrary.RestingFrame(frames, Standing(stature: 24));

        Assert.Equal(2, resting);
    }

    [Fact]
    public void RestingFrame_IsZeroWhenThereIsNoDeathStrip()
    {
        Assert.Equal(0, SpriteLibrary.RestingFrame([], Standing(stature: 24)));
    }

    /// <summary>
    /// Weighed by opacity rather than counted, because these strips fade a body out as
    /// well as flattening it — a ghost is not a corpse either.
    /// </summary>
    [Fact]
    public void FrameWeights_WeighAFrameByOpacityNotByPixelCount()
    {
        var solid = Sheet(width: 4, height: 4, opaque: (_, _) => true, alpha: 255);
        var faint = Sheet(width: 4, height: 4, opaque: (_, _) => true, alpha: 64);

        var solidWeight = Assert.Single(SpriteLibrary.FrameWeights(solid, 4, 4)).Pixels;
        var faintWeight = Assert.Single(SpriteLibrary.FrameWeights(faint, 4, 4)).Pixels;

        Assert.True(faintWeight < solidWeight, $"{faintWeight} should be lighter than {solidWeight}");
    }

    private static SpriteLibrary.Figure Standing(int stature) => new(0f, 0f, stature, 8);

    /// <summary>An RGBA8 sheet, four bytes to the pixel, exactly as Godot decodes one.</summary>
    private static byte[] Sheet(int width, int height, Func<int, int, bool> opaque, byte alpha = 255)
    {
        var data = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (opaque(x, y))
                {
                    data[(((y * width) + x) * 4) + 3] = alpha;
                }
            }
        }

        return data;
    }
}
