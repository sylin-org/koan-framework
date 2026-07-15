using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Testing.Integration;
using Koan.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Hosting;

[Collection(nameof(AggregateConfigHostOwnershipSpec))]
[CollectionDefinition(nameof(AggregateConfigHostOwnershipSpec), DisableParallelization = true)]
public sealed class AggregateConfigHostOwnershipSpec
{
    [Fact]
    public async Task Sequential_hosts_resolve_their_own_aggregate_config_and_repository_without_reset()
    {
        var firstFactory = new RecordingAdapterFactory();
        AggregateConfig<AggregateOwnershipEntity, string> firstConfig;
        IDataRepository<AggregateOwnershipEntity, string> firstRepository;

        await using (var first = await StartHost(firstFactory))
        {
            firstConfig = AggregateConfigs.Get<AggregateOwnershipEntity, string>(first.Services);
            firstRepository = firstConfig.Repository;

            firstFactory.CreateCalls.Should().Be(1);
            firstFactory.LastServices.Should().BeSameAs(first.Services);
        }

        var secondFactory = new RecordingAdapterFactory();
        await using var second = await StartHost(secondFactory);

        var secondConfig = AggregateConfigs.Get<AggregateOwnershipEntity, string>(second.Services);
        var secondRepository = secondConfig.Repository;

        secondConfig.Should().NotBeSameAs(firstConfig);
        secondRepository.Should().NotBeSameAs(firstRepository);
        secondFactory.CreateCalls.Should().Be(1);
        secondFactory.LastServices.Should().BeSameAs(second.Services);
    }

    [Fact]
    public async Task Parallel_hosts_keep_aggregate_configs_and_repositories_isolated()
    {
        var firstFactory = new RecordingAdapterFactory();
        var secondFactory = new RecordingAdapterFactory();
        await using var first = await StartHost(firstFactory);
        await using var second = await StartHost(secondFactory);

        var firstResolution = Task.Run(() =>
        {
            var config = AggregateConfigs.Get<AggregateOwnershipEntity, string>(first.Services);
            return (Config: config, Repository: config.Repository);
        });
        var secondResolution = Task.Run(() =>
        {
            var config = AggregateConfigs.Get<AggregateOwnershipEntity, string>(second.Services);
            return (Config: config, Repository: config.Repository);
        });

        var resolutions = await Task.WhenAll(firstResolution, secondResolution);

        resolutions[0].Config.Should().NotBeSameAs(resolutions[1].Config);
        resolutions[0].Repository.Should().NotBeSameAs(resolutions[1].Repository);
        firstFactory.CreateCalls.Should().Be(1);
        secondFactory.CreateCalls.Should().Be(1);
        firstFactory.LastServices.Should().BeSameAs(first.Services);
        secondFactory.LastServices.Should().BeSameAs(second.Services);
    }

    [Fact]
    public async Task One_host_memoizes_its_aggregate_config_and_repository()
    {
        var factory = new RecordingAdapterFactory();
        await using var host = await StartHost(factory);

        var first = AggregateConfigs.Get<AggregateOwnershipEntity, string>(host.Services);
        var second = AggregateConfigs.Get<AggregateOwnershipEntity, string>(host.Services);

        second.Should().BeSameAs(first);
        second.Repository.Should().BeSameAs(first.Repository);
        factory.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Diagnostics_report_the_configs_observed_by_the_current_host()
    {
        var factory = new RecordingAdapterFactory();
        await using var host = await StartHost(factory);
        var diagnostics = host.Services.GetRequiredService<IDataDiagnostics>();

        diagnostics.GetEntityConfigsSnapshot().Should().BeEmpty();

        _ = AggregateConfigs.Get<AggregateOwnershipEntity, string>(host.Services);

        diagnostics.GetEntityConfigsSnapshot().Should().ContainSingle(info =>
            info.EntityType == typeof(AggregateOwnershipEntity).FullName
            && info.KeyType == typeof(string).FullName
            && info.Provider == RecordingAdapterFactory.ProviderId
            && info.IdProperty == nameof(AggregateOwnershipEntity.Id));
    }

    private static Task<IntegrationHost> StartHost(RecordingAdapterFactory factory)
        => KoanIntegrationHost.Configure()
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory>(factory);
            })
            .StartAsync();

    [DataAdapter(RecordingAdapterFactory.ProviderId)]
    private sealed class AggregateOwnershipEntity : Koan.Data.Core.Model.Entity<AggregateOwnershipEntity>;

    private sealed class RecordingAdapterFactory : IDataAdapterFactory
    {
        public const string ProviderId = "aggregate-owner";

        private readonly NonIsolatingFakeAdapterFactory _inner = new();
        private IServiceProvider? _lastServices;
        private int _createCalls;

        public string Provider => ProviderId;

        public int CreateCalls => Volatile.Read(ref _createCalls);

        public IServiceProvider? LastServices => Volatile.Read(ref _lastServices);

        public bool CanHandle(string provider)
            => string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase);

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
        {
            Volatile.Write(ref _lastServices, sp);
            Interlocked.Increment(ref _createCalls);
            return _inner.Create<TEntity, TKey>(sp, source);
        }

        public StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => _inner.GetNamingCapability(services);
    }
}
