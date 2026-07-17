using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Model;
using Koan.Data.Core.Routing;
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
        registrations.AddSingleton<IDataAdapterFactory, OtherAdapterFactory>();
        registrations.AddSingleton<IDataDiagnostics, EmptyPublicDiagnostics>();

        using var services = registrations.BuildServiceProvider();
        services.GetRequiredService<DataDiagnostics>()
            .ObserveParticipation("participating-provider", "Archive");
        var publicDiagnostics = services.GetRequiredService<IDataDiagnostics>();
        publicDiagnostics.Should().BeOfType<EmptyPublicDiagnostics>();

        var health = new ProbeHealthContributor(
            services,
            publicDiagnostics);

        health.IsCritical.Should().BeTrue();
        var report = await health.Check();

        report.State.Should().Be(HealthState.Healthy);
        health.ActiveSources.Should().Equal("Archive");
    }

    [Fact]
    public async Task Configured_source_is_available_but_does_not_gate_readiness_until_selected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "other-provider",
                ["Koan:Data:Sources:Archive:Adapter"] = "participating-provider",
                ["Koan:Data:Sources:Archive:ConnectionString"] = "configured-but-unused"
            })
            .Build();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IConfiguration>(configuration);
        registrations.AddKoanDataCore();
        registrations.AddSingleton<IDataAdapterFactory, OtherAdapterFactory>();

        using var services = registrations.BuildServiceProvider();
        var health = new ProbeHealthContributor(
            services,
            services.GetRequiredService<IDataDiagnostics>());

        var available = await health.Check();

        health.IsCritical.Should().BeFalse();
        available.State.Should().Be(HealthState.Unknown);
        health.ActiveSources.Should().BeEmpty();

        services.GetRequiredService<DataDiagnostics>()
            .ObserveParticipation("participating-provider", "Archive");

        var selected = await health.Check();

        health.IsCritical.Should().BeTrue();
        selected.State.Should().Be(HealthState.Healthy);
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

    private sealed class OtherAdapterFactory : IDataAdapterFactory
    {
        private readonly NonIsolatingFakeAdapterFactory _inner = new();

        public string Provider => "other-provider";

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
        IDataDiagnostics diagnostics)
        : DataAdapterHealthContributorBase(
            "participating-provider",
            services,
            diagnostics,
            services.GetRequiredService<DataDefaultProviderPlan>())
    {
        public IReadOnlyCollection<string> ActiveSources { get; private set; } = [];

        protected override Task ProbeSource(string source, CancellationToken ct)
        {
            ActiveSources = [.. ActiveSources, source];
            return Task.CompletedTask;
        }
    }
}
