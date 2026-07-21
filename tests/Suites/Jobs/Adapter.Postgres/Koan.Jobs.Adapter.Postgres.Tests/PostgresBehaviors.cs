using Koan.Jobs;
using Koan.Jobs.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Jobs.Adapter.Postgres.Tests;

/// <summary>Runs the shared <see cref="JobBehaviorSuite"/> on a real Postgres store (ARCH-0079 convergence).</summary>
public sealed class PostgresBehaviors : JobBehaviorSuite, IClassFixture<PostgresJobsFixture>
{
    private readonly PostgresJobsFixture _fx;
    public PostgresBehaviors(PostgresJobsFixture fx) => _fx = fx;

    protected override Task<JobsHarness> CreateHostAsync(Action<JobsOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
        => JobsHarness.StartWithSettingsAsync(_fx.Settings, configure, configureServices);
}
