using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Core;
using Koan.Testing;
using Xunit;

namespace Koan.Data.Connector.Sqlite.Tests;

public class SqliteHealthTests : KoanTestBase
{
    private IServiceProvider BuildSqliteServices(string? connString)
    {
        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[] {
                    new KeyValuePair<string,string?>("Koan:Data:Sqlite:ConnectionString", connString)
                })
                .Build();
            services.AddSingleton<IConfiguration>(cfg);
            services.AddKoanCore(); // Required for health infrastructure
            services.AddSqliteAdapter();
            services.AddKoanDataCore();
        });
    }

    [Fact]
    public async Task Health_is_Healthy_when_connection_opens()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n") + ".db");
        var sp = BuildSqliteServices($"Data Source={file}");
        var hc = sp.GetRequiredService<IEnumerable<IHealthContributor>>().First(c => c.Name == "data:sqlite");
        var report = await hc.CheckAsync();
        report.State.Should().Be(Koan.Core.Observability.Health.HealthState.Healthy);
    }

    [Fact]
    public async Task Health_is_Unhealthy_when_connection_fails()
    {
        // Intentionally invalid path
        var sp = BuildSqliteServices("Data Source=Z:/non-existent-path/noway/never/app.db");
        var hc = sp.GetRequiredService<IEnumerable<IHealthContributor>>().First(c => c.Name == "data:sqlite");
        var report = await hc.CheckAsync();
        report.State.Should().Be(Koan.Core.Observability.Health.HealthState.Unhealthy);
    }
}

