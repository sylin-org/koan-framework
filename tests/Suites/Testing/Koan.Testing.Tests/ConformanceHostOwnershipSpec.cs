using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Koan.Testing.Tests;

[Collection(nameof(ConformanceHostOwnershipCollection))]
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

    [Fact]
    public async Task Concurrent_batteries_resolve_their_own_host()
    {
        const string firstOwner = "concurrent-first";
        const string secondOwner = "concurrent-second";
        var first = new OwnershipProbeConformance(firstOwner);
        var second = new OwnershipProbeConformance(secondOwner);

        try
        {
            await first.InitializeAsync();
            await second.InitializeAsync();

            await Task.WhenAll(
                first.RoundTrip_persists_and_reads_back_by_id(),
                second.RoundTrip_persists_and_reads_back_by_id());
        }
        finally
        {
            await second.DisposeAsync();
            await first.DisposeAsync();
        }

        ConformanceOwnershipProbeRegistrar.HasMarker(firstOwner).Should().BeFalse();
        ConformanceOwnershipProbeRegistrar.HasMarker(secondOwner).Should().BeFalse();
    }

    private sealed class OwnershipProbeConformance(string owner)
        : EntityConformanceSpecs<FakeWidget>
    {
        protected override FakeWidget NewValid()
        {
            var current = AppHost.Current;
            current.Should().NotBeNull();
            var configuration = current!.GetService(typeof(IConfiguration))
                .Should().BeAssignableTo<IConfiguration>().Subject!;
            configuration[ConformanceOwnershipProbeRegistrar.OwnerConfigurationKey]
                .Should().Be(owner, "each battery must remain bound to the host it created");
            return new() { Name = owner };
        }

        protected override void Configure(IDictionary<string, string?> settings)
            => settings[ConformanceOwnershipProbeRegistrar.OwnerConfigurationKey] = owner;
    }
}

[CollectionDefinition(nameof(ConformanceHostOwnershipCollection), DisableParallelization = true)]
public sealed class ConformanceHostOwnershipCollection;
