using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Observability.Health;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Testing.Integration;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.SqliteVec.Tests;

public sealed class SqliteVecParticipationSpec
{
    [HostScoped]
    public sealed class LocalVector : Entity<LocalVector> { }

    [Fact(DisplayName = "sqlite-vec pairs with SQLite placement and becomes critical only after vector use")]
    [Trait("Category", "Integration")]
    public async Task Pairs_with_sqlite_and_reports_runtime_participation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "koan-sqlitevec-participation", Guid.NewGuid().ToString("N"));
        var database = Path.Combine(directory, "paired.db");
        var settings = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={database};Pooling=False"
        };

        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSettings(settings)
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();
            AppHost.Current = host.Services;

            var health = host.Services.GetServices<IHealthContributor>()
                .Single(contributor => contributor.Name == "data:sqlitevec");

            Assert.False(health.IsCritical);
            Assert.Equal(HealthState.Unknown, (await health.Check()).State);
            Assert.False(File.Exists(database));

            await Vector<LocalVector>.Save("local-1", [1f, 0f, 0f]);

            Assert.True(health.IsCritical);
            Assert.Equal(HealthState.Healthy, (await health.Check()).State);
            Assert.True(File.Exists(database));
            Assert.NotNull(await Vector<LocalVector>.GetEmbedding("local-1"));
        }
        finally
        {
            AppHost.Current = null;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
