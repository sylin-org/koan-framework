using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// Verifies the query-dispatch failure path. Querying and counting live on the
/// <see cref="IQueryRepository{TEntity,TKey}"/> contract (one method over a <c>QueryDefinition</c>);
/// raw provider queries live on the optional <see cref="IRawQueryRepository{TEntity,TKey}"/> escape
/// hatch. When the adapter backing an entity implements neither, <c>Data&lt;T,K&gt;</c> must throw a
/// clear <see cref="NotSupportedException"/> naming the missing capability — not silently return empty
/// or fall back to a degraded path.
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
    public async Task Query_onAdapterWithoutQueryRepository_throwsNotSupportedWithDiagnostic()
    {
        using var scope = BuildScopeWithFakeRepo(new CrudOnlyRepo());

        Func<Task> act = () => Koan.Data.Core.Data<Widget, string>.Query((Widget w) => w.Name == "x");

        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.Which.Message.Should().Contain("IQueryRepository");
        ex.Which.Message.Should().Contain(nameof(Widget));
    }

    [Fact]
    public async Task QueryRaw_onAdapterWithoutRawSupport_throwsNotSupportedWithDiagnostic()
    {
        using var scope = BuildScopeWithFakeRepo(new CrudOnlyRepo());

        Func<Task> act = () => Koan.Data.Core.Data<Widget, string>.QueryRaw("SELECT * FROM Widget");

        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.Which.Message.Should().Contain("raw provider queries");
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

    /// <summary>
    /// CRUD-only adapter: implements <see cref="IDataRepository{TEntity,TKey}"/> but neither
    /// <see cref="IQueryRepository{TEntity,TKey}"/> nor <see cref="IRawQueryRepository{TEntity,TKey}"/>.
    /// </summary>
    private sealed class CrudOnlyRepo : IDataRepository<Widget, string>
    {
        public Task EnsureReady(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Widget?> Get(string id, CancellationToken ct = default) => Task.FromResult<Widget?>(null);
        public Task<IReadOnlyList<Widget?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Widget?>>(Array.Empty<Widget?>());
        public Task<Widget> Upsert(Widget model, CancellationToken ct = default) => Task.FromResult(model);
        public Task<int> UpsertMany(IEnumerable<Widget> models, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DeleteAll(CancellationToken ct = default) => Task.FromResult(0);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => Task.FromResult(0L);
        public IBatchSet<Widget, string> CreateBatch() => throw new NotImplementedException();
    }
}
