namespace SRDCombat.Core.Tests;

/// <summary>
/// Locates the repository from the test assembly's output directory, so a fixture can
/// be read from — and regenerated into — its committed source location rather than a
/// copy under <c>bin/</c>.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>The directory holding this project's committed test fixtures.</summary>
    public static string FixtureDirectory =>
        Path.Combine(RepositoryRoot, "tests", "SRDCombat.Core.Tests", "Fixtures");

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
