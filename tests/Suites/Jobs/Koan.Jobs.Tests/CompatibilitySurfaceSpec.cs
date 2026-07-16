using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Jobs.Tests;

public sealed class CompatibilitySurfaceSpec
{
    [Fact]
    public void Public_orchestrator_constructor_shape_remains_available()
    {
        typeof(JobCoordinator).IsNotPublic.Should().BeTrue(
            "applications compose IJobCoordinator; the host-owned implementation is not an authoring surface");

        typeof(JobOrchestrator).GetConstructor(
        [
            typeof(IJobLedger),
            typeof(JobTypeRegistry),
            typeof(IOptions<JobsOptions>),
            typeof(TimeProvider),
            typeof(ILogger<JobOrchestrator>),
            typeof(IServiceScopeFactory)
        ]).Should().NotBeNull();
    }
}
