namespace SRDCombat.Content.Tests;

/// <summary>
/// The committed SRD corpus, loaded once for every test class that shares this holder.
/// </summary>
/// <remarks>
/// Mirrors <c>SRDCombat.Game.Tests.TestContent</c> (#473), for the same reason and the
/// same issue (#319): before this, every content-hungry class in this project carried
/// its own <c>private static readonly SrdContent Content = ContentLoader.Load(...)</c>,
/// so the corpus was parsed once per class rather than once per assembly. A class that
/// reads <see cref="Srd"/> instead of calling <c>ContentLoader.Load</c> itself joins the
/// shared load rather than adding another. The two holders cannot themselves be shared —
/// <c>Game.Tests</c> and <c>Content.Tests</c> are separate assemblies — so each project
/// carries its own copy of this seam.
/// </remarks>
internal static class TestContent
{
    /// <summary>The real committed content, loaded on first use and never again.</summary>
    public static SrdContent Srd { get; } = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);
}
