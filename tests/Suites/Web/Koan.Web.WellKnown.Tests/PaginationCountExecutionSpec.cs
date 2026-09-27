using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.InMemory;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

public sealed class PaginationCountExecutionSpec
{
    [Fact]
    public async Task IncludeCount_controls_count_execution_and_total_dependent_headers()
    {
        using var host = await Start();
        AppHost.Current = host.Services;
        await PaginationCountProbeEntity.RemoveAll();
        for (var i = 0; i < 25; i++)
            await new PaginationCountProbeEntity { Id = $"row-{i:D2}" }.Save();

        PaginationCountProbe.Reset();
        var client = host.GetTestClient();
        var withoutCount = await client.GetAsync("/pagination-without-count?page=1&pageSize=20");

        withoutCount.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadCount(withoutCount)).Should().Be(20);
        withoutCount.Headers.GetValues("X-Page").Should().ContainSingle().Which.Should().Be("1");
        withoutCount.Headers.GetValues("X-Page-Size").Should().ContainSingle().Which.Should().Be("20");
        withoutCount.Headers.Contains("X-Total-Count").Should().BeFalse();
        withoutCount.Headers.Contains("X-Total-Pages").Should().BeFalse();
        withoutCount.Headers.Contains("Link").Should().BeFalse();
        PaginationCountProbe.TotalRequests.Should().Be(0);

        PaginationCountProbe.Reset();
        var withCount = await client.GetAsync("/pagination-with-count?page=1&pageSize=20");

        withCount.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadCount(withCount)).Should().Be(20);
        withCount.Headers.GetValues("X-Total-Count").Should().ContainSingle().Which.Should().Be("25");
        withCount.Headers.GetValues("X-Total-Pages").Should().ContainSingle().Which.Should().Be("2");
        PaginationCountProbe.TotalRequests.Should().Be(1);

        PaginationCountProbe.Reset();
        var withoutPagination = await client.GetAsync("/pagination-off");

        withoutPagination.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        PaginationCountProbe.TotalRequests.Should().Be(0);

        await host.StopAsync();
    }

    private static async Task<int> ReadCount(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetArrayLength();
    }

    private static async Task<IHost> Start()
    {
        var host = Host.CreateDefaultBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.UseEnvironment("Test");
            web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Test",
                    ["Koan:Data:Sources:Default:Adapter"] = CountingInMemoryAdapterFactory.ProviderId,
                    ["Koan:Data:Sources:Default:ConnectionString"] = "memory://pagination-count-execution",
                    ["Koan:Web:Pagination:MaxPageSize"] = "20",
                    ["Koan:Web:Pagination:AbsoluteMaxRecords"] = "20",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning"
                }));
            web.ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory, CountingInMemoryAdapterFactory>();
                services.AddKoanControllersFrom<PaginationWithoutCountController>();
            });
            web.Configure(_ => { });
        }).Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }
}

[Access(read: Access.Anyone)]
public sealed class PaginationCountProbeEntity : Entity<PaginationCountProbeEntity>;

[Route("pagination-without-count")]
[Pagination(Mode = PaginationMode.Required, DefaultSize = 20, MaxSize = 20, IncludeCount = false)]
public sealed class PaginationWithoutCountController : EntityController<PaginationCountProbeEntity>;

[Route("pagination-with-count")]
[Pagination(Mode = PaginationMode.Required, DefaultSize = 20, MaxSize = 20)]
public sealed class PaginationWithCountController : EntityController<PaginationCountProbeEntity>;

[Route("pagination-off")]
[Pagination(Mode = PaginationMode.Off)]
public sealed class PaginationOffController : EntityController<PaginationCountProbeEntity>;

internal static class PaginationCountProbe
{
    private static int _totalRequests;

    public static int TotalRequests => Volatile.Read(ref _totalRequests);

    public static void Record() => Interlocked.Increment(ref _totalRequests);

    public static void Reset() => Interlocked.Exchange(ref _totalRequests, 0);
}

internal sealed class CountingInMemoryAdapterFactory : IDataAdapterFactory
{
    public const string ProviderId = "counting-inmemory";
    private readonly InMemoryAdapterFactory _inner = new();

    public string Provider => ProviderId;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) =>
        _inner.GetNamingCapability(services);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider services, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => new CountingRepository<TEntity, TKey>(_inner.Create<TEntity, TKey>(services, source));
}

internal sealed class CountingRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IBoundedQueryRepository<TEntity, TKey>,
    IDescribesCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataRepository<TEntity, TKey> _inner;
    private readonly IQueryRepository<TEntity, TKey> _query;
    private readonly IBoundedQueryRepository<TEntity, TKey> _bounded;

    public CountingRepository(IDataRepository<TEntity, TKey> inner)
    {
        _inner = inner;
        _query = (IQueryRepository<TEntity, TKey>)inner;
        _bounded = (IBoundedQueryRepository<TEntity, TKey>)inner;
    }

    public void Describe(ICapabilities capabilities) =>
        ((IDescribesCapabilities)_inner).Describe(capabilities);

    public Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        if (query.CountStrategy is not null) PaginationCountProbe.Record();
        return _query.Query(query, ct);
    }

    public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        PaginationCountProbe.Record();
        return _query.Count(query, ct);
    }

    public Task<BoundedQueryResult<TEntity>> QueryBoundedCandidates(
        QueryDefinition query, int maxCandidates, CancellationToken ct = default)
    {
        if (query.CountStrategy is not null) PaginationCountProbe.Record();
        return _bounded.QueryBoundedCandidates(query, maxCandidates, ct);
    }

    public Task EnsureReady(CancellationToken ct = default) => _inner.EnsureReady(ct);
    public Task<TEntity?> Get(TKey id, CancellationToken ct = default) => _inner.Get(id, ct);
    public Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default) => _inner.GetMany(ids, ct);
    public Task<TEntity> Upsert(TEntity model, CancellationToken ct = default) => _inner.Upsert(model, ct);
    public Task<bool> Delete(TKey id, CancellationToken ct = default) => _inner.Delete(id, ct);
    public Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default) => _inner.UpsertMany(models, ct);
    public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default) => _inner.DeleteMany(ids, ct);
    public Task<int> DeleteAll(CancellationToken ct = default) => _inner.DeleteAll(ct);
    public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => _inner.RemoveAll(strategy, ct);
    public IBatchSet<TEntity, TKey> CreateBatch() => _inner.CreateBatch();
}
