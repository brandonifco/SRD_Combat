namespace SRDCombat.Game.Tests;

/// <summary>
/// Placeholder covering the skeleton itself: the test project builds and can
/// load the assembly it is written against. Replaced by real coverage as soon
/// as SRDCombat.Game has behaviour to test.
/// </summary>
public class ProjectReferenceTests
{
    [Fact]
    public void SubjectAssembly_IsReferenced()
    {
        var assembly = typeof(SRDCombat.Game.AssemblyMarker).Assembly;

        Assert.Equal("SRDCombat.Game", assembly.GetName().Name);
    }
}
