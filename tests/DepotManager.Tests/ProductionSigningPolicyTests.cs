using DepotManager;
using Xunit;

namespace Depot.Tests;

public sealed class ProductionSigningPolicyTests
{
    [Theory]
    [InlineData("0.15.172-preview", false)]
    [InlineData("0.15.172-preview+abc123", false)]
    [InlineData("0.15.172", true)]
    [InlineData("1.0.0+abc123", true)]
    public void RequiresPublisherContinuityForVersion_SeparatesPreviewAndStable(string informationalVersion, bool expected)
    {
        Assert.Equal(expected, ProductionSigningPolicy.RequiresPublisherContinuityForVersion(informationalVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiresPublisherContinuityForVersion_DoesNotTreatMissingDevelopmentVersionAsStable(string? informationalVersion)
    {
        Assert.False(ProductionSigningPolicy.RequiresPublisherContinuityForVersion(informationalVersion));
    }
}
