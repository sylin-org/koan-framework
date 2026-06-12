using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Jobs;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the Jobs pillar (per ARCH-0079, JOBS-0005). Proves the orchestrator's public entry point
/// (<see cref="IJobCoordinator"/>) and the ledger resolve through real <c>AddKoan()</c> reflective discovery.
/// </summary>
public sealed class JobsPillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public JobsPillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_wires_the_jobs_pillar_through_real_bootstrap()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        host.Services.GetRequiredService<IJobCoordinator>().Should().NotBeNull();
        host.Services.GetRequiredService<IJobLedger>().Should().NotBeNull();

        // The worker's boot summary ("[Koan.Jobs] ledger=… · N job types · M scheduled · claim=…") and the
        // module's boot-report both read the registry; prove those reads resolve and never throw.
        var registry = host.Services.GetRequiredService<JobTypeRegistry>();
        registry.Should().NotBeNull();
        var options = host.Services.GetRequiredService<IOptions<JobsOptions>>().Value;
        var bootSummary = () => registry.All.Sum(b => b.ScheduledActions(options).Count());
        bootSummary.Should().NotThrow();
    }
}
