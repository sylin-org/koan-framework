using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Decorators;
using Koan.Data.Core.Model;
using Koan.Data.SoftDelete;
using Koan.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.SoftDelete.Tests;

/// <summary>
/// NK-289: a mass soft delete is one native bulk write, not a loop of single upserts. A store that refuses the write
/// part-way through must not leave some rows soft-deleted and others not. A decorator inside the facade counts the
/// native writes and fails the third row a single upsert would carry, or the whole bulk write that carries it.
/// </summary>
public sealed class SoftDeleteMassWriteSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [SoftDelete]
    public sealed class Ledger : Entity<Ledger> { public string Title { get; set; } = ""; }

    private const int FailAt = 3;
    private static volatile Writes? _writes;

    private sealed class Writes
    {
        public int Singles;
        public int Bulks;
        public bool Armed;
    }

    [Fact(DisplayName = "A mass soft delete is one bulk write: a refused write leaves no row half-deleted (NK-289)")]
    public async Task Mass_soft_delete_is_one_bulk_write()
    {
        RequireBackingStore();
        await using var host = await BootAsync(services =>
            services.AddSingleton<IDataRepositoryDecorator, CountingDecorator>());
        using var _ = Lease(NewPartition());
        try
        {
            _writes = new Writes();
            var ids = new List<string>();
            for (var index = 0; index < 5; index++) ids.Add((await new Ledger { Title = $"row-{index}" }.Save()).Id);

            _writes = new Writes { Armed = true };
            var act = () => Ledger.Remove(ids);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*refused*");
            (await Ledger.All()).Should().HaveCount(5, "a refused mass soft delete must not hide part of the set");

            _writes = new Writes();
            (await Ledger.Remove(ids)).Should().Be(5);
            _writes.Bulks.Should().Be(1, "the whole set is one native bulk write");
            _writes.Singles.Should().Be(0);
            (await Ledger.All()).Should().BeEmpty();
            using (Ledger.WithDeleted()) (await Ledger.All()).Should().HaveCount(5);
        }
        finally
        {
            _writes = null;
        }
    }

    private sealed class CountingDecorator : IDataRepositoryDecorator
    {
        public object? TryDecorate(Type entityType, Type keyType, object repository, IServiceProvider services)
            => entityType == typeof(Ledger) && keyType == typeof(string)
                ? new CountingRepository((IDataRepository<Ledger, string>)repository)
                : null;
    }

    private sealed class CountingRepository(IDataRepository<Ledger, string> inner) :
        IDataRepository<Ledger, string>,
        IQueryRepository<Ledger, string>,
        IDescribesCapabilities
    {
        private IQueryRepository<Ledger, string> Queries => (IQueryRepository<Ledger, string>)inner;

        public void Describe(ICapabilities capabilities) => DataCaps.Describe(inner, inner.GetType().Name).CopyInto(capabilities);

        public Task<Ledger> Upsert(Ledger model, CancellationToken ct = default)
        {
            if (_writes is { } writes && ++writes.Singles == FailAt && writes.Armed)
                throw new InvalidOperationException("The store refused the write.");
            return inner.Upsert(model, ct);
        }

        public Task<int> UpsertMany(IEnumerable<Ledger> models, CancellationToken ct = default)
        {
            var list = models.ToList();
            if (_writes is { } writes && ++writes.Bulks > 0 && writes.Armed && list.Count >= FailAt)
                throw new InvalidOperationException("The store refused the write.");
            return inner.UpsertMany(list, ct);
        }

        public Task<RepositoryQueryResult<Ledger>> Query(QueryDefinition query, CancellationToken ct = default) => Queries.Query(query, ct);
        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default) => Queries.Count(query, ct);
        public Task<Ledger?> Get(string id, CancellationToken ct = default) => inner.Get(id, ct);
        public Task<IReadOnlyList<Ledger?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default) => inner.GetMany(ids, ct);
        public Task<bool> Delete(string id, CancellationToken ct = default) => inner.Delete(id, ct);
        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default) => inner.DeleteMany(ids, ct);
        public Task<int> DeleteAll(CancellationToken ct = default) => inner.DeleteAll(ct);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => inner.RemoveAll(strategy, ct);
        public IBatchSet<Ledger, string> CreateBatch() => inner.CreateBatch();
        public Task EnsureReady(CancellationToken ct = default) => inner.EnsureReady(ct);
    }
}
