using FluentAssertions;
using Koan.ZenGarden.Core;
using Koan.ZenGarden.Extensions;
using Koan.ZenGarden.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ZenGardenInitializationProviderTests
{
    [Fact]
    public async Task ResolveAsync_returns_ready_offering_and_maps_endpoint()
    {
        var snapshots = new[]
        {
            CreateSnapshot("offering:mongodb", ready: false, "mongodb://standby:27017"),
            CreateSnapshot("offering:mongodb", ready: true, "mongodb://primary:27018")
        };

        await using var provider = BuildScope(
            new StubZenGardenClient(snapshots),
            new StubBinding("mongo", "mongodb"));

        var initializationProvider = provider.GetRequiredService<IZenGardenInitializationProvider>();
        var intent = ZenGardenConnectionIntent.ForOffering("mongodb");

        var resolved = await initializationProvider.ResolveAsync(intent);

        resolved.Should().NotBeNull();
        resolved!.ToolFqid.Should().Be("offering:mongodb");
        resolved.Offering.Should().Be("mongodb");
        resolved.GetUri("mongodb").Should().Be("mongodb://primary:27018");
    }

    [Fact]
    public async Task ResolveAsync_returns_null_when_offering_is_not_ready()
    {
        var snapshots = new[]
        {
            CreateSnapshot("offering:mongodb", ready: false, "mongodb://standby:27017")
        };

        await using var provider = BuildScope(
            new StubZenGardenClient(snapshots),
            new StubBinding("mongo", "mongodb"));

        var initializationProvider = provider.GetRequiredService<IZenGardenInitializationProvider>();
        var resolved = await initializationProvider.ResolveAsync(ZenGardenConnectionIntent.ForOffering("mongodb"));

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_without_instance_selector_can_bind_ready_instance_candidate()
    {
        var snapshots = new[]
        {
            CreateSnapshot("offering:mongodb:dev", ready: true, "mongodb://dev-primary:27019")
        };

        await using var provider = BuildScope(
            new StubZenGardenClient(snapshots),
            new StubBinding("mongo", "mongodb"));

        var initializationProvider = provider.GetRequiredService<IZenGardenInitializationProvider>();
        var resolved = await initializationProvider.ResolveAsync(ZenGardenConnectionIntent.ForOffering("mongodb"));

        resolved.Should().NotBeNull();
        resolved!.ToolFqid.Should().Be("offering:mongodb:dev");
        resolved.Offering.Should().Be("mongodb");
        resolved.Instance.Should().Be("dev");
        resolved.GetUri("mongodb").Should().Be("mongodb://dev-primary:27019");
    }

    [Fact]
    public async Task ResolveAsync_without_instance_selector_can_bind_ready_alias_candidate()
    {
        var snapshots = new[]
        {
            CreateSnapshot(
                "offering:ollama@adopted",
                ready: true,
                "http://ollama-adopted:11434",
                "offering:ollama")
        };

        await using var provider = BuildScope(
            new StubZenGardenClient(snapshots),
            new StubBinding("ollama", "ollama"));

        var initializationProvider = provider.GetRequiredService<IZenGardenInitializationProvider>();
        var resolved = await initializationProvider.ResolveAsync(ZenGardenConnectionIntent.ForOffering("ollama"));

        resolved.Should().NotBeNull();
        resolved!.ToolFqid.Should().Be("offering:ollama@adopted");
        resolved.Offering.Should().Be("ollama@adopted");
        resolved.GetUri("http").Should().Be("http://ollama-adopted:11434");
    }

    [Fact]
    public void TryGetDefaultOffering_uses_registered_bindings()
    {
        using var provider = BuildScope(
            new StubZenGardenClient(Array.Empty<ZenGardenToolSnapshot>()),
            new StubBinding("mongo", "mongodb"));

        var initializationProvider = provider.GetRequiredService<IZenGardenInitializationProvider>();

        initializationProvider.TryGetDefaultOffering("mongo", out var offering).Should().BeTrue();
        offering.Should().Be("mongodb");
    }

    [Fact]
    public async Task WishCapabilitiesAsync_returns_receipt()
    {
        await using var provider = BuildScope(
            new StubZenGardenClient(Array.Empty<ZenGardenToolSnapshot>()),
            new StubBinding("ollama", "ollama"));

        var initializationProvider = provider.GetRequiredService<IZenGardenInitializationProvider>();
        var receipt = await initializationProvider.WishCapabilitiesAsync(
            ZenGardenConnectionIntent.ForOffering("ollama", capabilities: ["llama3.2", "nomic-embed-text"]));

        receipt.Should().NotBeNull();
        receipt!.OfferingSelector.Should().Be("ollama");
        receipt.ToolFqid.Should().Be("offering:ollama");
        receipt.Requested.Should().Contain(["llama3.2", "nomic-embed-text"]);
    }

    private static ServiceProvider BuildScope(
        IZenGardenClient client,
        params IZenGardenOfferingBinding[] bindings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(client);
        foreach (var binding in bindings)
        {
            services.AddSingleton<IZenGardenOfferingBinding>(binding);
        }

        services.AddKoanZenGarden();
        return services.BuildServiceProvider();
    }

    private static ZenGardenToolSnapshot CreateSnapshot(string toolFqid, bool ready, string uri, params string[] aliases)
    {
        return new ZenGardenToolSnapshot
        {
            ToolFqid = toolFqid,
            ToolType = ZenGardenToolType.Offering,
            Ready = ready,
            State = ready ? ZenGardenToolState.Ready : ZenGardenToolState.Unavailable,
            Revision = ready ? 2 : 1,
            Aliases = aliases,
            Connection = new ZenGardenConnection
            {
                Uris = new[] { uri }
            }
        };
    }

    private sealed class StubBinding(string adapterId, string offering) : IZenGardenOfferingBinding
    {
        public string AdapterId { get; } = adapterId;
        public string Offering { get; } = offering;
    }

    private sealed class StubZenGardenClient : IZenGardenClient
    {
        private readonly IReadOnlyList<ZenGardenToolSnapshot> _snapshots;

        public StubZenGardenClient(IReadOnlyList<ZenGardenToolSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        public IDisposable Subscribe(
            ZenGardenSubscription subscription,
            Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
            ZenGardenWatchOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ZenGardenToolSnapshot>> CatalogAsync(
            ZenGardenSubscription subscription,
            CancellationToken cancellationToken = default)
        {
            var results = _snapshots
                .Where(subscription.Matches)
                .Where(subscription.RequirementsSatisfiedBy)
                .ToArray();
            return Task.FromResult<IReadOnlyList<ZenGardenToolSnapshot>>(results);
        }

        public ValueTask<ZenGardenCapabilityWish> WishAsync(
            string offering,
            IReadOnlyList<string> capabilities,
            ZenGardenCapabilityWishOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var normalizedSelector = offering.Trim().ToLowerInvariant();
            var toolFqid = normalizedSelector.StartsWith("offering:", StringComparison.OrdinalIgnoreCase)
                ? normalizedSelector
                : $"offering:{normalizedSelector}";

            var wish = new ZenGardenCapabilityWish
            {
                RequestId = Guid.NewGuid().ToString("N"),
                ToolFqid = toolFqid,
                OfferingSelector = normalizedSelector.StartsWith("offering:", StringComparison.OrdinalIgnoreCase)
                    ? normalizedSelector["offering:".Length..]
                    : normalizedSelector,
                Requested = capabilities,
                Missing = capabilities,
                Status = "requested",
                IsFulfilled = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return ValueTask.FromResult(wish);
        }

        public IDisposable SubscribeCapability(
            string requestId,
            Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
            ZenGardenCapabilityWatchOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public IDisposable SubscribeCapability(
            Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
            ZenGardenCapabilityWatchOptions? options = null)
        {
            throw new NotSupportedException();
        }

        public bool TryGetCapabilityWish(string requestId, out ZenGardenCapabilityWish wish)
        {
            wish = default!;
            return false;
        }

        public bool TryGetCurrent(string toolFqid, out ZenGardenToolSnapshot snapshot)
        {
            snapshot = _snapshots.FirstOrDefault(s => string.Equals(s.ToolFqid, toolFqid, StringComparison.OrdinalIgnoreCase))!;
            return snapshot is not null;
        }

        public void Dispose()
        {
        }
    }
}
