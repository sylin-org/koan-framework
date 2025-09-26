using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Core.Tests;

public class QueryWithCountTests
{
    [Fact]
    public async Task UnpagedRequestsRespectAbsoluteMaximum()
    {
        var repo = new StubRepository();
        repo.Seed(Enumerable.Range(0, 10).Select(i => new StubEntity { Id = i.ToString(), Name = $"Item-{i}" }));

        using var scope = WithRepository(repo);

        var result = await Data<StubEntity, string>.QueryWithCount(
            query: null,
            options: null,
            ct: CancellationToken.None,
            absoluteMaxRecords: 5);

        result.ExceededSafetyLimit.Should().BeTrue();
        result.Items.Should().BeEmpty();
        repo.CountCalls.Should().Be(1);
        repo.QueryCalls.Should().Be(0, because: "the repository should not materialize the items when the cap is exceeded");
    }

    [Fact]
    public async Task PagedRepositoriesShortCircuitToPagedPipeline()
    {
        var repo = new StubRepository();
        repo.Seed(Enumerable.Range(1, 12).Select(i => new StubEntity { Id = i.ToString(), Name = $"Item-{i}" }));

        using var scope = WithRepository(repo);

        var options = new DataQueryOptions().WithPagination(page: 2, pageSize: 3);
        var result = await Data<StubEntity, string>.QueryWithCount((Expression<Func<StubEntity, bool>>?)null, options, CancellationToken.None);

        result.RepositoryHandledPagination.Should().BeTrue();
        result.Items.Should().HaveCount(3).And.Contain(e => e.Id == "4");
        result.TotalCount.Should().Be(12);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);

        repo.PagedCalls.Should().Be(1);
        repo.QueryCalls.Should().Be(0);
    }

    [Fact]
    public void HasPaginationRequiresPositivePageAndSize()
    {
        var onlyPage = new DataQueryOptions(page: 1, pageSize: null);
        onlyPage.HasPagination.Should().BeFalse();

        var onlySize = new DataQueryOptions(page: null, pageSize: 25);
        onlySize.HasPagination.Should().BeFalse();

        var zeroPage = new DataQueryOptions(page: 0, pageSize: 25);
        zeroPage.HasPagination.Should().BeFalse();

        var valid = new DataQueryOptions(page: 2, pageSize: 25);
        valid.HasPagination.Should().BeTrue();

        var noPagination = valid.WithoutPagination();
        noPagination.HasPagination.Should().BeFalse();
    }

    private static IDisposable WithRepository(StubRepository repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataService>(new StubDataService(repo));
        var provider = services.BuildServiceProvider();

        var previous = AppHost.Current;
        AppHost.Current = provider;
        return new DelegateDisposable(() => AppHost.Current = previous);
    }

    private sealed class StubDataService : IDataService
    {
        private readonly StubRepository _repository;

        public StubDataService(StubRepository repository)
        {
            _repository = repository;
        }

        public IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
        {
            if (typeof(TEntity) != typeof(StubEntity))
            {
                throw new InvalidOperationException($"Unknown entity {typeof(TEntity).Name}");
            }

            return (IDataRepository<TEntity, TKey>)(object)_repository;
        }

        public Direct.IDirectSession Direct(string sourceOrAdapter) => throw new NotImplementedException();

        public Koan.Data.Vector.Abstractions.IVectorSearchRepository<TEntity, TKey>? TryGetVectorRepository<TEntity, TKey>()
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
            => null;
    }

    private sealed class StubRepository :
        IDataRepository<StubEntity, string>,
        IDataRepositoryWithOptions<StubEntity, string>,
        IPagedRepository<StubEntity, string>
    {
        private readonly List<StubEntity> _items = new();

        public int QueryCalls { get; private set; }
        public int CountCalls { get; private set; }
        public int PagedCalls { get; private set; }

        public void Seed(IEnumerable<StubEntity> items)
        {
            _items.Clear();
            _items.AddRange(items);
        }

        public Task<StubEntity?> GetAsync(string id, CancellationToken ct = default)
            => Task.FromResult<StubEntity?>(_items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<StubEntity>> QueryAsync(object? query, CancellationToken ct = default)
        {
            QueryCalls++;
            return Task.FromResult<IReadOnlyList<StubEntity>>(_items.ToList());
        }

        public Task<IReadOnlyList<StubEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
        {
            if (options?.HasPagination == true)
            {
                var skip = Math.Max(options.Page!.Value - 1, 0) * options.PageSize!.Value;
                return Task.FromResult<IReadOnlyList<StubEntity>>(_items.Skip(skip).Take(options.PageSize.Value).ToList());
            }

            return QueryAsync(query, ct);
        }

        public Task<PagedRepositoryResult<StubEntity>> QueryPageAsync(object? query, DataQueryOptions options, CancellationToken ct = default)
        {
            PagedCalls++;
            var skip = Math.Max(options.Page!.Value - 1, 0) * options.PageSize!.Value;
            var pageItems = _items.Skip(skip).Take(options.PageSize.Value).ToList();
            return Task.FromResult(new PagedRepositoryResult<StubEntity>
            {
                Items = pageItems,
                TotalCount = _items.Count,
                Page = options.Page ?? 1,
                PageSize = options.PageSize ?? pageItems.Count
            });
        }

        public Task<int> CountAsync(object? query, CancellationToken ct = default)
        {
            CountCalls++;
            return Task.FromResult(_items.Count);
        }

        public Task<StubEntity> UpsertAsync(StubEntity model, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> UpsertManyAsync(IEnumerable<StubEntity> models, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> DeleteAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public IBatchSet<StubEntity, string> CreateBatch() => throw new NotImplementedException();
    }

    private sealed class StubEntity : IEntity<string>
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _onDispose;
        public DelegateDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
