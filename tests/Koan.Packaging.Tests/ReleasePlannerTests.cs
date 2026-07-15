using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleasePlannerTests
{
    [Theory]
    [InlineData("[0.17.3, 0.18.0)", "0.17.3")]
    [InlineData("0.17.3", "0.17.3")]
    [InlineData("(, 1.0.0)", null)]
    public void MinimumVersionParsesNuGetRanges(string range, string? expected) =>
        Assert.Equal(expected, PackagePipeline.MinimumVersion(range));

    [Theory]
    [InlineData("[0.17.3, 0.18.0)", true)]
    [InlineData("[1.2.3, 2.0.0)", true)]
    [InlineData("0.17.3", false)]
    [InlineData("[0.17.3, )", false)]
    [InlineData("[0.17.3, 0.19.0)", false)]
    [InlineData("(0.17.3, 0.18.0)", false)]
    public void CompatibilityBandsAreExact(string range, bool expected) =>
        Assert.Equal(expected, PackagePipeline.IsExpectedCompatibilityBand(range));

}
