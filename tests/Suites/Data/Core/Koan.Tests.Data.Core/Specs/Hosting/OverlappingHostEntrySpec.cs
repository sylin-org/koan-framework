using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Koan.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Hosting;

/// <summary>
/// One process serves one host unless a flow says otherwise: hosts that overlap each enter their own, and an Entity static
/// then runs that host's Lifecycle hooks and no other's, in parallel flows.
/// </summary>
[Collection(nameof(AggregateConfigHostOwnershipSpec))]
public sealed class OverlappingHostEntrySpec
{
    [Fact]
    public async Task Each_flow_runs_the_hooks_of_the_host_it_entered()
    {
        var firstSeen = new List<string>();
        var secondSeen = new List<string>();
        await using var first = await StartHost(firstSeen);
        await using var second = await StartHost(secondSeen);

        await Task.WhenAll(Run(first, "first"), Run(second, "second"));

        firstSeen.Should().Equal("first");
        secondSeen.Should().Equal("second");

        static Task Run(IntegrationHost host, string id) => Task.Run(async () =>
        {
            using var entered = host.Enter();
            await new OverlapNote { Id = id }.Save();
        });
    }

    private static Task<IntegrationHost> StartHost(List<string> seen) =>
        KoanIntegrationHost.Configure()
            .ConfigureServices(services =>
            {
                services.AddKoan(() => OverlapNote.Lifecycle.BeforeUpsert(context =>
                {
                    lock (seen) seen.Add(context.Current.Id);
                    return context.Proceed();
                }));
                services.AddSingleton<IDataAdapterFactory>(new OverlapAdapterFactory());
            })
            .StartAsync();

    [DataAdapter(OverlapAdapterFactory.ProviderId)]
    private sealed class OverlapNote : Entity<OverlapNote>;

    private sealed class OverlapAdapterFactory : IDataAdapterFactory
    {
        public const string ProviderId = "overlap-owner";
        private readonly NonIsolatingFakeAdapterFactory _inner = new();
        public string Provider => ProviderId;
        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull => _inner.Create<TEntity, TKey>(sp, source);
        public Koan.Data.Abstractions.Naming.StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => _inner.GetNamingCapability(services);
    }
}
