namespace SRDCombat.Viewer.Tests;

/// <summary>
/// Locates the repository from the test assembly's output directory, the same trick
/// <c>SRDCombat.Core.Tests.RepositoryPaths</c> uses — kept as this project's own copy
/// rather than a shared reference, because this project's whole point (see its own
/// project file's doc comment) is to add nothing to the corpus-loading machinery
/// <c>RepositoryPaths.SrdContentDirectory</c> belongs to. This one only ever points at
/// <c>client/assets/sprites</c> and this project's own fixtures.
/// </summary>
internal static class ViewerRepositoryPaths
{
    /// <summary>The shipped sprite sheets a regeneration can silently reshape (#467).</summary>
    public static string SpritesDirectory =>
        Path.Combine(RepositoryRoot, "client", "assets", "sprites");

    /// <summary>This project's own committed fixtures — the geometry manifest among them.</summary>
    public static string FixtureDirectory =>
        Path.Combine(RepositoryRoot, "tests", "SRDCombat.Viewer.Tests", "Fixtures");

    private static string RepositoryRoot { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SRDCombat.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find SRDCombat.sln above '{AppContext.BaseDirectory}'.");
    }
}
