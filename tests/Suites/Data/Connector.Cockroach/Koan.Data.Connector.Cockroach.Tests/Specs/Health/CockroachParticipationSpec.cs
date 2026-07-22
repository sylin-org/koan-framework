using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Cockroach.Tests.Specs.Health;

public sealed class CockroachParticipationSpec
{
    [Fact]
    public async Task Available_but_unelected_connector_is_non_critical_and_connection_free()
    {
        using var services = Services(includeHigherPriorityAdapter: true);
        var contributor = Contributor(services, new StubDiagnostics());

        var report = await contributor.Check();

        report.State.Should().Be(HealthState.Unknown);
        contributor.IsCritical.Should().BeFalse();
    }

    [Fact]
    public void Runtime_participation_makes_cockroach_a_readiness_dependency()
    {
        using var services = Services(includeHigherPriorityAdapter: true);
        var contributor = Contributor(
            services,
            new StubDiagnostics([
                new DataAdapterParticipationInfo(Infrastructure.Constants.Provider.Name, "Archive")
            ]));

        contributor.IsCritical.Should().BeTrue();
    }

    private static CockroachHealthContributor Contributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics)
    {
        var providers = new DataProviderCatalog(services.GetServices<IDataAdapterFactory>(), null);
        var registry = new DataSourceRegistry();
        var defaultProvider = new DataDefaultProviderPlan(providers, registry);
        return new CockroachHealthContributor(services, diagnostics, providers, defaultProvider);
    }

    private static ServiceProvider Services(bool includeHigherPriorityAdapter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataAdapterFactory, CockroachAdapterFactory>();
        if (includeHigherPriorityAdapter)
        {
            services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
        }

        return services.BuildServiceProvider();
    }

    private sealed class StubDiagnostics(
        IReadOnlyList<DataAdapterParticipationInfo>? participations = null) : IDataDiagnostics
    {
        public IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot() => [];
        public IReadOnlyList<DataAdapterParticipationInfo> GetAdapterParticipationsSnapshot() => participations ?? [];
    }

    [ProviderPriority(100)]
    private sealed class HigherPriorityAdapter : IDataAdapterFactory
    {
        public string Provider => "higher-priority";

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
            IServiceProvider sp,
            string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull =>
            throw new NotSupportedException("The selection-only adapter cannot create repositories.");

        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
    }
}
