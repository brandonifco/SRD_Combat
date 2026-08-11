namespace SRDCombat.Content.Tests;

/// <summary>
/// Placeholder covering the skeleton itself: the test project builds and can
/// load the assembly it is written against. Replaced by real coverage as soon
/// as SRDCombat.Content has behaviour to test.
/// </summary>
public class ProjectReferenceTests
{
    [Fact]
    public void SubjectAssembly_IsReferenced()
    {
        var assembly = typeof(SRDCombat.Content.AssemblyMarker).Assembly;

        Assert.Equal("SRDCombat.Content", assembly.GetName().Name);
    }
}
