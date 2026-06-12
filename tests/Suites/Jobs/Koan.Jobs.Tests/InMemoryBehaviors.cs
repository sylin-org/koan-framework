using Koan.Jobs;
using Koan.Jobs.TestKit;

namespace Koan.Jobs.Tests;

/// <summary>Runs the shared <see cref="JobBehaviorSuite"/> on the in-memory tier.</summary>
public sealed class InMemoryBehaviors : JobBehaviorSuite
{
    protected override Task<JobsHarness> CreateHostAsync(Action<JobsOptions>? configure = null)
        => JobsHarness.StartInMemoryAsync(configure);
}
