namespace SRDCombat.TestSupport;

/// <summary>
/// Locates the repository from a test assembly's output directory, so tests can read
/// the real committed content — and regenerate fixtures into their committed source
/// location — rather than a copy under <c>bin/</c>. The single home for the root-finding
/// that every test project used to copy (#318).
/// </summary>
public static class RepositoryPaths
{
    /// <summary>The repository root: the directory holding <c>SRDCombat.sln</c>.</summary>
    public static string RepositoryRoot { get; } = FindRoot();

    /// <summary>The directory holding the SRD content files.</summary>
    public static string SrdContentDirectory => Path.Combine(RepositoryRoot, "data", "srd");

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
