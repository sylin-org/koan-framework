using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Sqlite.Tests;

public class SqliteHealthTests
{
    private static IServiceProvider BuildServices(string? connString)
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("Koan:Data:Sqlite:ConnectionString", connString)
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter();
        sc.AddKoanDataCore();
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task Health_is_Healthy_when_connection_opens()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n") + ".db");
        var sp = BuildServices($"Data Source={file}");
        var hc = sp.GetRequiredService<IEnumerable<IHealthContributor>>().First(c => c.Name == "data:sqlite");
        var report = await hc.CheckAsync();
        report.State.Should().Be(Koan.Core.Observability.Health.HealthState.Healthy);
    }

    [Fact]
    public async Task Health_is_Unhealthy_when_connection_fails()
    {
        // Intentionally invalid path
        var sp = BuildServices("Data Source=Z:/non-existent-path/noway/never/app.db");
        var hc = sp.GetRequiredService<IEnumerable<IHealthContributor>>().First(c => c.Name == "data:sqlite");
        var report = await hc.CheckAsync();
        report.State.Should().Be(Koan.Core.Observability.Health.HealthState.Unhealthy);
    }
}
