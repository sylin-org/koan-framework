using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Testing.Tests;

public sealed class ConformanceHostOwnershipSpec
{
    [Fact]
    public async Task Conformance_lifecycle_preserves_newer_startup_and_overlapping_host_owners()
    {
        AppHost.Current.Should().BeNull();

        const string olderOwner = "older";
        const string newerOwner = "newer";
        var older = new OwnershipProbeConformance(olderOwner);
        var newer = new OwnershipProbeConformance(newerOwner);

        try
        {
            await older.InitializeAsync();
            var olderMarker = ConformanceOwnershipProbeRegistrar.GetRequiredMarker(olderOwner);
            AppHost.Current.Should().BeSameAs(olderMarker);

            await newer.InitializeAsync();
            var newerMarker = ConformanceOwnershipProbeRegistrar.GetRequiredMarker(newerOwner);
            AppHost.Current.Should().BeSameAs(newerMarker);

            await older.DisposeAsync();
            AppHost.Current.Should().BeSameAs(newerMarker);
        }
        finally
        {
            await newer.DisposeAsync();
            await older.DisposeAsync();
        }

        AppHost.Current.Should().BeNull();
        ConformanceOwnershipProbeRegistrar.HasMarker(olderOwner).Should().BeFalse();
        ConformanceOwnershipProbeRegistrar.HasMarker(newerOwner).Should().BeFalse();
    }

    private sealed class OwnershipProbeConformance(string owner)
        : EntityConformanceSpecs<FakeWidget>
    {
        protected override FakeWidget NewValid() => new() { Name = owner };

        protected override void Configure(IDictionary<string, string?> settings)
            => settings[ConformanceOwnershipProbeRegistrar.OwnerConfigurationKey] = owner;
    }
}
