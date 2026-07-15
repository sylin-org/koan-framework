using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Jobs.Tests;

public sealed class CompatibilitySurfaceSpec
{
    [Fact]
    public void Public_017_infrastructure_constructor_shapes_remain_available()
    {
        typeof(JobCoordinator).GetConstructor(
        [
            typeof(IJobLedger),
            typeof(JobTypeRegistry),
            typeof(JobOrchestrator),
            typeof(IJobTransport),
            typeof(IServiceProvider),
            typeof(IOptions<JobsOptions>),
            typeof(TimeProvider)
        ]).Should().NotBeNull();

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
