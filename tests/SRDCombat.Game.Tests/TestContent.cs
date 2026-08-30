using SRDCombat.Content;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The committed SRD corpus, loaded once for every test class that shares this holder.
/// </summary>
/// <remarks>
/// <para>
/// #319's complaint is that the corpus is loaded once per test class — a <c>static
/// readonly</c> field in each — so a suite of eighteen content-hungry classes in this
/// project alone parses 330 monsters and 339 spells eighteen times. This is the seam that
/// issue's fixture will flip: a class that reads <see cref="Srd"/> instead of calling
/// <c>ContentLoader.Load</c> itself joins the shared load rather than adding another.
/// </para>
/// <para>
/// It is introduced here rather than filed as future work because #473 adds two
/// content-hungry test classes at once, and two more loads is the wrong direction. To
/// keep the slice from adding even one, <c>RunSaveTests</c> — the on-disk-format class
/// these two are modelled on — is pointed at the same holder in the same commit.
/// #319 then converted every remaining class in this project onto <see cref="Srd"/>, so
/// the corpus now loads exactly once for the whole assembly.
/// </para>
/// </remarks>
internal static class TestContent
{
    /// <summary>The real committed content, loaded on first use and never again.</summary>
    public static SrdContent Srd { get; } = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);
}
