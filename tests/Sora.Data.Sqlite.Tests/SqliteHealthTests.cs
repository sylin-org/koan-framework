using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Core;
using Sora.Data.Core;
using Sora.Data.Sqlite;
using Xunit;

namespace Sora.Data.Sqlite.Tests;

public class SqliteHealthTests
{
    private static IServiceProvider BuildServices(string? connString)
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("Sora:Data:Sqlite:ConnectionString", connString)
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
    sc.AddSqliteAdapter();
    sc.AddSoraDataCore();
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task Health_is_Healthy_when_connection_opens()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("n") + ".db");
        var sp = BuildServices($"Data Source={file}");
        var hc = sp.GetRequiredService<System.Collections.Generic.IEnumerable<IHealthContributor>>().First(c => c.Name == "data:sqlite");
        var report = await hc.CheckAsync();
        report.State.Should().Be(HealthState.Healthy);
    }

    [Fact]
    public async Task Health_is_Unhealthy_when_connection_fails()
    {
        // Intentionally invalid path
        var sp = BuildServices("Data Source=Z:/non-existent-path/noway/never/app.db");
        var hc = sp.GetRequiredService<System.Collections.Generic.IEnumerable<IHealthContributor>>().First(c => c.Name == "data:sqlite");
        var report = await hc.CheckAsync();
        report.State.Should().Be(HealthState.Unhealthy);
    }
}
