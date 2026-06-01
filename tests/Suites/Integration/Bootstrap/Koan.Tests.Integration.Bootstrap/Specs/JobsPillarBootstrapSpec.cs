using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Jobs.Events;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Jobs pillar (per ARCH-0079). Proves <c>IJobEventPublisher</c> resolves
/// through real <c>AddKoan()</c> reflective discovery.
/// </summary>
/// <remarks>
/// <c>IJobCoordinator</c> and <c>IJobStoreResolver</c> would be more representative "Jobs is
/// alive" targets but both are <c>internal</c>; <c>IJobEventPublisher</c> is the
/// highest-level public singleton the registrar adds, so it's the closest public proof the
/// pillar wired up. The Jobs auto-registrar also wires four hosted services (worker,
/// sweeper, archival); the default <c>InMemoryJobQueue</c> is offline-safe — workers poll an
/// empty queue without external dependencies.
/// </remarks>
public sealed class JobsPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public JobsPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_resolves_IJobEventPublisher_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            // Offline-only — see DataCorePillarBootstrapSpec remarks.
            .WithSetting("Koan:Data:Redis:ConnectionString", "localhost:0")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var publisher = host.Services.GetRequiredService<IJobEventPublisher>();
        publisher.Should().NotBeNull();
    }
}
