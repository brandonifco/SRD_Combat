using SRDCombat.TestSupport;

namespace SRDCombat.Core.Tests;

/// <summary>
/// This project's own committed test fixtures, located off the shared
/// <see cref="RepositoryPaths.RepositoryRoot"/> (#318). Kept local because the path
/// points at this project's tree, not at anything a second project shares.
/// </summary>
internal static class CoreRepositoryPaths
{
    /// <summary>The directory holding this project's committed test fixtures.</summary>
    public static string FixtureDirectory =>
        Path.Combine(RepositoryPaths.RepositoryRoot, "tests", "SRDCombat.Core.Tests", "Fixtures");
}
