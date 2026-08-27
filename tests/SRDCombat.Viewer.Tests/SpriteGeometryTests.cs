namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The asset-side half of the geometry contract #467 asks for. <c>SpriteMeasurementTests</c>
/// pins the *reading* rules — given a canvas, how big is the figure and where does it
/// stand. This pins the *asset*: what canvas each shipped sheet actually is, so a
/// regeneration that changes one is caught by a failing test rather than by Brandon's
/// own eyes in a live fight, which is how PR #461 found it (regenerating 17 sheets
/// through the committed pipeline silently changed every canvas size — the Ogre alone
/// went 169×169 → 119×64 — and nothing in CI noticed).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a committed manifest rather than deriving an expectation from the pipeline.</b>
/// The shipped sheets predate the current pipeline settings (see
/// <c>tools/asset_pipeline/master_to_sprite.py</c>'s module docstring) — running the
/// pipeline today and asserting its output matches what is shipped would fail on nearly
/// every sheet in the roster right now, for a reason that has nothing to do with this
/// gate's job. What this gate owns is narrower and cheaper: whatever is shipped stays
/// exactly what is shipped, unless a human deliberately updates the manifest alongside
/// the art — the same discipline the frozen transcript uses for gameplay behaviour.
/// </para>
/// <para>
/// <b>No Godot in this file.</b> <see cref="PngGeometry"/> reads width/height straight
/// out of each PNG's header bytes, never through <c>Godot.Image</c> — see this
/// project's own doc comment on why a native Godot object can't be constructed outside
/// the engine at all, let alone in a test host running dozens of them.
/// </para>
/// </remarks>
public class SpriteGeometryTests
{
    [Fact]
    public void ShippedSheets_MatchTheCommittedGeometryManifest()
    {
        var manifestPath = Path.Combine(ViewerRepositoryPaths.FixtureDirectory, SpriteGeometryManifest.FixtureName);

        Assert.True(
            File.Exists(manifestPath),
            $"Missing fixture '{manifestPath}'. Regenerate it with SpriteGeometryManifestWriter.");

        var expected = SpriteGeometryManifest.Load(manifestPath);
        var actual = SpriteGeometryManifest.MeasureShippedTree();

        var expectedPaths = expected.Select(e => e.RelativePath).ToHashSet(StringComparer.Ordinal);
        var actualPaths = actual.Select(a => a.RelativePath).ToHashSet(StringComparer.Ordinal);

        var missing = expectedPaths.Except(actualPaths).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var added = actualPaths.Except(expectedPaths).OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} manifested sheet(s) no longer exist on disk: {string.Join(", ", missing)}. " +
            "If this is deliberate (art removed), regenerate the manifest with SpriteGeometryManifestWriter.");

        Assert.True(added.Count == 0,
            $"{added.Count} sheet(s) on disk are not in the committed manifest: {string.Join(", ", added)}. " +
            "New art ships as a dropped file, but its geometry still needs pinning — regenerate the manifest " +
            "with SpriteGeometryManifestWriter and review the new lines.");

        var actualByPath = actual.ToDictionary(a => a.RelativePath, StringComparer.Ordinal);
        var mismatches = expected
            .Where(e => actualByPath[e.RelativePath] != e)
            .Select(e => $"{e.RelativePath}: manifest says {e.Width}x{e.Height} ({e.FrameCount} frame(s)), " +
                         $"disk says {actualByPath[e.RelativePath].Width}x{actualByPath[e.RelativePath].Height} " +
                         $"({actualByPath[e.RelativePath].FrameCount} frame(s))")
            .ToList();

        Assert.True(mismatches.Count == 0,
            "A regeneration changed shipped sheet geometry without updating the manifest " +
            $"(#467's own failure mode — see PR #461):\n{string.Join("\n", mismatches)}");
    }
}

/// <summary>
/// Regenerates the committed sprite geometry manifest from whatever is actually shipped
/// under <c>client/assets/sprites</c> right now. Un-skip, run, re-skip, and review the
/// diff before committing — exactly <c>TranscriptWriter</c>'s discipline, and for the
/// same reason: a manifest that rewrites itself on every test run gates nothing, and a
/// geometry change slipping through in the same commit as its own "fix" is the one
/// failure mode this file exists to prevent.
/// </summary>
public class SpriteGeometryManifestWriter
{
    [Fact(Skip = "Writes the committed fixture. Un-skip, run, re-skip, and review the diff.")]
    public void WriteSpriteGeometryManifest()
    {
        Directory.CreateDirectory(ViewerRepositoryPaths.FixtureDirectory);

        var path = Path.Combine(ViewerRepositoryPaths.FixtureDirectory, SpriteGeometryManifest.FixtureName);
        SpriteGeometryManifest.Write(path, SpriteGeometryManifest.MeasureShippedTree());
    }
}
