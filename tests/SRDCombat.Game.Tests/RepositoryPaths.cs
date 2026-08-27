namespace SRDCombat.Game.Tests;

/// <summary>
/// Locates the repository from a test assembly's output directory, so tests can read
/// the real committed content rather than a fixture copied out of it.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>The directory holding the SRD content files.</summary>
    public static string SrdContentDirectory => Path.Combine(RepositoryRoot, "data", "srd");

    /// <summary>The committed scenario library (#473, design §5a).</summary>
    public static string ScenarioDirectory => Path.Combine(RepositoryRoot, ScenarioFile.DirectoryName);

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

        throw new DirectoryNotFoundException(
            $"Could not find SRDCombat.sln above '{AppContext.BaseDirectory}'.");
    }
}
