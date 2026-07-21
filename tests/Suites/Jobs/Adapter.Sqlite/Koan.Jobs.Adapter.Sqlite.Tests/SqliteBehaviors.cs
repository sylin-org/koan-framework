using Koan.Jobs;
using Koan.Jobs.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Adapter.Sqlite.Tests;

/// <summary>Runs the shared <see cref="JobBehaviorSuite"/> on a real SQLite store — same assertions, durable tier.</summary>
public sealed class SqliteBehaviors : JobBehaviorSuite
{
    protected override Task<JobsHarness> CreateHostAsync(Action<JobsOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
        => JobsHarness.StartSqliteAsync(configure, configureServices);
}
