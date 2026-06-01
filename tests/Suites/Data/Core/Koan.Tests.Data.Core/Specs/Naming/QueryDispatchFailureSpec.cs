using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// Verifies the dispatch failure path introduced in DATA-0095 Phase 1b. When an adapter does
/// not implement the typed query marker interface (ILinq* / IString*WithOptions), <c>Data&lt;T,K&gt;</c>
/// must throw a clear NotSupportedException identifying the missing capability — not silently
/// return empty results or fall back to a degraded path.
/// </summary>
[Collection(nameof(QueryDispatchFailureSpec))]
[CollectionDefinition(nameof(QueryDispatchFailureSpec), DisableParallelization = true)]
public class QueryDispatchFailureSpec
{
    public class Widget : Koan.Data.Core.Model.Entity<Widget>
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public async Task StringQuery_onLinqOnlyAdapter_throwsNotSupportedWithDiagnostic()
    {
        using var scope = BuildScopeWithFakeRepo(new LinqOnlyRepo());

        Func<Task> act = () => Koan.Data.Core.Data<Widget, string>.Query("some-raw-string");

        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.Which.Message.Should().Contain("string queries");
        ex.Which.Message.Should().Contain(nameof(Widget));
    }

    [Fact]
    public async Task LinqQuery_onStringOnlyAdapter_throwsNotSupportedWithDiagnostic()
    {
        using var scope = BuildScopeWithFakeRepo(new StringOnlyRepo());

        Func<Task> act = () => Koan.Data.Core.Data<Widget, string>.Query((Widget w) => w.Name == "x");

        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.Which.Message.Should().Contain("ILinqQueryRepositoryWithOptions");
        ex.Which.Message.Should().Contain(nameof(Widget));
    }

    private static IDisposable BuildScopeWithFakeRepo(IDataRepository<Widget, string> repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataService>(new FakeDataService(repo));
        var sp = services.BuildServiceProvider();
        return AppHost.PushScope(sp);
    }

    private sealed class FakeDataService : IDataService
    {
        private readonly IDataRepository<Widget, string> _repo;
        public FakeDataService(IDataRepository<Widget, string> repo) => _repo = repo;

        public IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
            => (IDataRepository<TEntity, TKey>)(object)_repo;

        public Koan.Data.Core.Direct.IDirectSession Direct(string? source = null, string? adapter = null)
            => throw new NotImplementedException();
    }

    /// <summary>Adapter that supports only ILinqQueryRepository — no string query support.</summary>
    private sealed class LinqOnlyRepo
        : IDataRepository<Widget, string>,
          ILinqQueryRepositoryWithOptions<Widget, string>
    {
        public Task EnsureReady(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Widget?> Get(string id, CancellationToken ct = default) => Task.FromResult<Widget?>(null);
        public Task<IReadOnlyList<Widget?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Widget?>>(Array.Empty<Widget?>());
        public Task<CountResult> Count(CountRequest<Widget> request, CancellationToken ct = default)
            => Task.FromResult(new CountResult(0, false));
        public Task<Widget> Upsert(Widget model, CancellationToken ct = default) => Task.FromResult(model);
        public Task<int> UpsertMany(IEnumerable<Widget> models, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DeleteAll(CancellationToken ct = default) => Task.FromResult(0);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => Task.FromResult(0L);
        public IBatchSet<Widget, string> CreateBatch() => throw new NotImplementedException();

        public Task<RepositoryQueryResult<Widget>> Query(
            Expression<Func<Widget, bool>>? predicate,
            DataQueryOptions? options,
            CancellationToken ct = default)
            => Task.FromResult(RepositoryQueryResult<Widget>.Unhandled(Array.Empty<Widget>()));

        public Task<IReadOnlyList<Widget>> Query(Expression<Func<Widget, bool>> predicate, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Widget>>(Array.Empty<Widget>());
    }

    /// <summary>Adapter that supports only IStringQueryRepository — no LINQ predicate support.</summary>
    private sealed class StringOnlyRepo
        : IDataRepository<Widget, string>,
          IStringQueryRepositoryWithOptions<Widget, string>
    {
        public Task EnsureReady(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Widget?> Get(string id, CancellationToken ct = default) => Task.FromResult<Widget?>(null);
        public Task<IReadOnlyList<Widget?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Widget?>>(Array.Empty<Widget?>());
        public Task<CountResult> Count(CountRequest<Widget> request, CancellationToken ct = default)
            => Task.FromResult(new CountResult(0, false));
        public Task<Widget> Upsert(Widget model, CancellationToken ct = default) => Task.FromResult(model);
        public Task<int> UpsertMany(IEnumerable<Widget> models, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DeleteAll(CancellationToken ct = default) => Task.FromResult(0);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => Task.FromResult(0L);
        public IBatchSet<Widget, string> CreateBatch() => throw new NotImplementedException();

        public Task<RepositoryQueryResult<Widget>> Query(
            string query,
            DataQueryOptions? options,
            CancellationToken ct = default)
            => Task.FromResult(RepositoryQueryResult<Widget>.Unhandled(Array.Empty<Widget>()));

        public Task<RepositoryQueryResult<Widget>> Query(
            string query,
            object? parameters,
            DataQueryOptions? options,
            CancellationToken ct = default)
            => Task.FromResult(RepositoryQueryResult<Widget>.Unhandled(Array.Empty<Widget>()));

        public Task<IReadOnlyList<Widget>> Query(string query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Widget>>(Array.Empty<Widget>());
    }
}
