using SRDCombat.Game;
using SRDCombat.TestSupport;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Paths specific to this project, located off the shared
/// <see cref="RepositoryPaths.RepositoryRoot"/> (#318). Kept local because
/// <see cref="ScenarioDirectory"/> names itself from a product type
/// (<see cref="ScenarioFile.DirectoryName"/>) that the reference-free shared library
/// deliberately does not depend on. Corpus paths still come from
/// <see cref="RepositoryPaths"/>.
/// </summary>
internal static class GameRepositoryPaths
{
    /// <summary>The committed scenario library (#473, design §5a).</summary>
    public static string ScenarioDirectory =>
        Path.Combine(RepositoryPaths.RepositoryRoot, ScenarioFile.DirectoryName);
}
