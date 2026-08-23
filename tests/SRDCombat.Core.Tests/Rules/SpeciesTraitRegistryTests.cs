using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Rules;

/// <summary>
/// The species trait registry: empty today, on purpose — see its own remarks. These
/// pin that state so an accidental entry (or, once #291's follow-on work lands one for
/// real, an accidental removal) is caught rather than drifting silently.
/// </summary>
public class SpeciesTraitRegistryTests
{
    [Fact]
    public void NothingIsImplementedYet()
    {
        Assert.Empty(SpeciesTraitRegistry.ImplementedNames);
    }

    [Theory]
    [InlineData("Darkvision")]
    [InlineData("Breath Weapon")]
    [InlineData("Resourceful")]
    [InlineData("Stonecunning")]
    public void EveryPrintedNameResolvesToNothing(string name)
    {
        Assert.Null(SpeciesTraitRegistry.Resolve(name));
        Assert.False(SpeciesTraitRegistry.Implements(name));
    }

    [Fact]
    public void ResolveAndImplementsRejectNullNames()
    {
        Assert.Throws<ArgumentNullException>(() => SpeciesTraitRegistry.Resolve(null!));
        Assert.Throws<ArgumentNullException>(() => SpeciesTraitRegistry.Implements(null!));
    }
}
