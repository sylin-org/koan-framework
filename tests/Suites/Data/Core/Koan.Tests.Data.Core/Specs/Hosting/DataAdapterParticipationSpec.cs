using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Model;
using Koan.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Hosting;

public sealed class DataAdapterParticipationSpec
{
    [Fact]
    public void Repository_use_records_canonical_provider_and_every_logical_source()
    {
        using var services = Services();
        var data = services.GetRequiredService<IDataService>();
        var diagnostics = services.GetRequiredService<IDataDiagnostics>();

        _ = data.GetScopeDiagnostics<AliasEntity, string>();
        diagnostics.GetAdapterParticipationsSnapshot().Should().BeEmpty(
            "capability inspection must not turn an available adapter into a runtime dependency");

        _ = data.GetRepository<AliasEntity, string>();
        using (EntityContext.Source("Archive"))
        {
            _ = data.GetRepository<ArchiveEntity, string>();
        }

        diagnostics.GetAdapterParticipationsSnapshot().Should().BeEquivalentTo(
        [
            new DataAdapterParticipationInfo(CanonicalAdapterFactory.ProviderId, "Default"),
            new DataAdapterParticipationInfo(CanonicalAdapterFactory.ProviderId, "Archive")
        ]);
    }

    [Fact]
    public async Task Health_reads_the_internal_runtime_ledger_when_public_diagnostics_are_replaced()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "other-provider"
            })
            .Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IConfiguration>(configuration);
        registrations.AddKoanDataCore();
        registrations.AddSingleton<IDataDiagnostics, EmptyPublicDiagnostics>();

        using var services = registrations.BuildServiceProvider();
        services.GetRequiredService<DataDiagnostics>()
            .ObserveParticipation("participating-provider", "Archive");
        var publicDiagnostics = services.GetRequiredService<IDataDiagnostics>();
        publicDiagnostics.Should().BeOfType<EmptyPublicDiagnostics>();

        var health = new ProbeHealthContributor(
            services,
            services.GetRequiredService<DataSourceRegistry>(),
            publicDiagnostics);

        health.IsCritical.Should().BeTrue();
        var report = await health.Check();

        report.State.Should().Be(HealthState.Healthy);
        health.ActiveSources.Should().Equal("Archive");
    }

    private static ServiceProvider Services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Archive:Adapter"] = CanonicalAdapterFactory.Alias,
                ["Koan:Data:Sources:Archive:ConnectionString"] = "unused"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKoanDataCore();
        services.AddSingleton<IDataAdapterFactory, CanonicalAdapterFactory>();
        return services.BuildServiceProvider();
    }

    [DataAdapter(CanonicalAdapterFactory.Alias)]
    private sealed class AliasEntity : Entity<AliasEntity>;

    private sealed class ArchiveEntity : Entity<ArchiveEntity>;

    private sealed class CanonicalAdapterFactory : IDataAdapterFactory
    {
        public const string ProviderId = "canonical-provider";
        public const string Alias = "friendly-alias";
        private readonly NonIsolatingFakeAdapterFactory _inner = new();

        public string Provider => ProviderId;
        public IReadOnlyCollection<string> Aliases => [Alias];

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
            IServiceProvider sp,
            string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
            => _inner.Create<TEntity, TKey>(sp, source);

        public StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => _inner.GetNamingCapability(services);
    }

    private sealed class EmptyPublicDiagnostics : IDataDiagnostics
    {
        public IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot() => [];
    }

    private sealed class ProbeHealthContributor(
        IServiceProvider services,
        DataSourceRegistry sourceRegistry,
        IDataDiagnostics diagnostics)
        : DataAdapterHealthContributorBase(
            "participating-provider",
            services,
            sourceRegistry,
            diagnostics)
    {
        public IReadOnlyCollection<string> ActiveSources { get; private set; } = [];

        protected override Task<HealthReport> CheckActive(
            IReadOnlyCollection<string> sources,
            CancellationToken ct)
        {
            ActiveSources = sources;
            return Task.FromResult(new HealthReport(
                Name,
                HealthState.Healthy,
                "participating",
                null,
                null));
        }
    }
}
