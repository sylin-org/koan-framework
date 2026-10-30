using Koan.Jobs;
using Koan.Jobs.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Tests;

/// <summary>Runs the shared <see cref="JobBehaviorSuite"/> on the in-memory tier.</summary>
public sealed class InMemoryBehaviors : JobBehaviorSuite
{
    protected override Task<JobsHarness> CreateHostAsync(Action<JobsOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
        => JobsHarness.StartInMemoryAsync(configure, configureServices);
}
