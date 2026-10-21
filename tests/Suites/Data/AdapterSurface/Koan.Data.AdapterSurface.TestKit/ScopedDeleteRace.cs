using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Decorators;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// Cross-adapter race ORACLE for scoped deletes (DATA-0156; ledger NK-168, NK-181, NK-182, NK-303). A row leaves the
/// caller's scope between the facade's check and the store's act: scope A's row is deleted and scope B saves a row with
/// the same id. A decorator inside the facade injects that interleaving at whichever comes first, the facade's
/// visibility read returning or the delete reaching the store, so the oracle measures both the check-then-delete path
/// and the store-scoped one. A delete that carries the scope into the store removes nothing and B's row survives.
/// </summary>
public static class ScopedDeleteRace
{
    private const string Field = "__race";
    private static readonly AsyncLocal<string?> _scope = new();
    // One armed interleaving at a time; the oracle runs its legs sequentially inside one spec.
    private static volatile Interleaving? _race;

    public sealed class RaceDoc : Entity<RaceDoc> { public string Title { get; set; } = ""; }

    /// <summary>Declares the managed discriminator and the interleaving decorator while the owning host composes.</summary>
    public static void Declare() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: Field,
        ClrType: typeof(string),
        ValueProvider: () => _scope.Value,
        AppliesTo: static type => type == typeof(RaceDoc),
        RequiredCapability: DataCaps.Isolation.RowScoped));

    public static void Configure(IServiceCollection services)
        => services.AddSingleton<IDataRepositoryDecorator, InterleavingDecorator>();

    /// <summary>Single, many and batch deletes under scope A each lose their row to scope B mid-flight; none deletes it.</summary>
    public static async Task AssertAtomicAsync()
    {
        try
        {
            using var _part = EntityContext.Partition("race-" + Guid.CreateVersion7().ToString("n"));
            using var _legs = new AwesomeAssertions.Execution.AssertionScope();   // report every leg that fails

            var single = await Seed("single");
            using (Scope("A")) (await RaceDoc.Remove(single)).Should().BeFalse("the row left scope A before the store acted");
            await AssertOwnedByB(single);

            var many = await Seed("many");
            using (Scope("A")) (await RaceDoc.Remove(new[] { many })).Should().Be(0, "the row left scope A before the store acted");
            await AssertOwnedByB(many);

            var batched = await Seed("batch");
            using (Scope("A"))
            {
                var result = await RaceDoc.Batch().Delete(batched).Save();
                result.Deleted.Should().Be(0, "the row left scope A before the store acted");
                if (result.HasCompleteItemOutcomes)
                    result.Items.Select(item => item.Outcome).Should().Equal(BatchItemOutcome.Missing);
            }
            await AssertOwnedByB(batched);
        }
        finally
        {
            _scope.Value = null;
            _race = null;
        }
    }

    /// <summary>A guarded delete under a scope carries the scope too: it cannot remove another scope's row and is no
    /// longer refused for the scope's sake.</summary>
    public static async Task AssertConditionalDeleteCarriesScopeAsync()
    {
        try
        {
            using var _part = EntityContext.Partition("race-if-" + Guid.CreateVersion7().ToString("n"));
            RaceDoc mine, theirs;
            using (Scope("A")) mine = await new RaceDoc { Title = "same" }.Save();
            using (Scope("B")) theirs = await new RaceDoc { Title = "same" }.Save();

            using (Scope("A"))
            {
                (await Data<RaceDoc, string>.DeleteIf(theirs.Id, row => row.Title == "same")).Should().BeFalse();
                (await Data<RaceDoc, string>.DeleteIf(mine.Id, row => row.Title == "same")).Should().BeTrue();
            }
            using (Scope("B")) (await RaceDoc.Get(theirs.Id)).Should().NotBeNull("scope A's guarded delete must not remove it");
        }
        finally
        {
            _scope.Value = null;
        }
    }

    /// <summary>The axis report says whether this adapter carries the scope into its deletes or checks first.</summary>
    public static void AssertDeclared(IServiceProvider services, bool atStore)
    {
        var adapter = Koan.Data.Core.Axes.DataAxis.Explain<RaceDoc>(services).Adapter;
        adapter.Should().NotBeNull();
        adapter!.ScopedDeletesAtStore.Should().Be(atStore);
    }

    /// <summary>Saves A's row and arms the interleaving that moves its id to B.</summary>
    private static async Task<string> Seed(string title)
    {
        RaceDoc row;
        using (Scope("A")) row = await new RaceDoc { Title = title }.Save();
        _race = new Interleaving(row.Id);
        return row.Id;
    }

    private static async Task AssertOwnedByB(string id)
    {
        _race!.Fired.Should().BeTrue("the oracle must have interleaved the competing write");
        _race = null;
        RaceDoc? survivor;
        using (Scope("B")) survivor = await RaceDoc.Get(id);
        (survivor?.Title).Should().Be("owned-by-b", "scope A's delete must not remove scope B's row");
    }

    private static IDisposable Scope(string id)
    {
        var prev = _scope.Value;
        _scope.Value = id;
        return new Pop(() => _scope.Value = prev);
    }

    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    private sealed class Interleaving(string id)
    {
        public bool Fired { get; private set; }

        /// <summary>Once: A's row goes, and B saves the same id, written straight to the adapter under B's stamp.</summary>
        public async Task Fire(IDataRepository<RaceDoc, string> adapter, CancellationToken ct)
        {
            if (Fired) return;
            Fired = true;
            await adapter.Delete(id, ct);
            using (ManagedFieldWriteScope.Enter(new Dictionary<string, object?>(StringComparer.Ordinal) { [Field] = "B" }))
                await adapter.Upsert(new RaceDoc { Id = id, Title = "owned-by-b" }, ct);
        }
    }

    private static Task Interleave(IDataRepository<RaceDoc, string> adapter, CancellationToken ct)
        => _race is { } race ? race.Fire(adapter, ct) : Task.CompletedTask;

    private sealed class InterleavingDecorator : IDataRepositoryDecorator
    {
        public object? TryDecorate(Type entityType, Type keyType, object repository, IServiceProvider services)
            => entityType == typeof(RaceDoc) && keyType == typeof(string)
                ? new InterleavingRepository((IDataRepository<RaceDoc, string>)repository)
                : null;
    }

    private sealed class InterleavingRepository(IDataRepository<RaceDoc, string> inner) :
        IDataRepository<RaceDoc, string>,
        IQueryRepository<RaceDoc, string>,
        IConditionalDeleteRepository<RaceDoc, string>,
        IDescribesCapabilities
    {
        private IQueryRepository<RaceDoc, string> Queries => (IQueryRepository<RaceDoc, string>)inner;
        private IConditionalDeleteRepository<RaceDoc, string> Conditional => (IConditionalDeleteRepository<RaceDoc, string>)inner;

        public void Describe(ICapabilities capabilities) => DataCaps.Describe(inner, inner.GetType().Name).CopyInto(capabilities);

        // The check: the facade's visibility read returns, and the row then leaves scope.
        public async Task<RepositoryQueryResult<RaceDoc>> Query(QueryDefinition query, CancellationToken ct = default)
        {
            var result = await Queries.Query(query, ct);
            await Interleave(inner, ct);
            return result;
        }

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default) => Queries.Count(query, ct);

        // The act: the row leaves scope just before the store executes the delete.
        public async Task<bool> Delete(string id, CancellationToken ct = default)
        {
            await Interleave(inner, ct);
            return await inner.Delete(id, ct);
        }

        public async Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var list = ids.ToList();
            await Interleave(inner, ct);
            return await inner.DeleteMany(list, ct);
        }

        public async Task<bool> ConditionalDeleteAsync(string id, Filter guard, CancellationToken ct = default)
        {
            await Interleave(inner, ct);
            return await Conditional.ConditionalDeleteAsync(id, guard, ct);
        }

        public async Task<int> ConditionalDeleteMany(IReadOnlyList<string> ids, Filter guard, CancellationToken ct = default)
        {
            await Interleave(inner, ct);
            return await Conditional.ConditionalDeleteMany(ids, guard, ct);
        }

        public IBatchSet<RaceDoc, string> CreateBatch()
        {
            var batch = inner.CreateBatch();
            return batch is IConditionalBatchDelete<RaceDoc, string> guarded
                ? new GuardedInterleavingBatch(inner, batch, guarded)
                : new InterleavingBatch(inner, batch);
        }

        public Task<RaceDoc?> Get(string id, CancellationToken ct = default) => inner.Get(id, ct);
        public Task<IReadOnlyList<RaceDoc?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default) => inner.GetMany(ids, ct);
        public Task<RaceDoc> Upsert(RaceDoc model, CancellationToken ct = default) => inner.Upsert(model, ct);
        public Task<int> UpsertMany(IEnumerable<RaceDoc> models, CancellationToken ct = default) => inner.UpsertMany(models, ct);
        public Task<int> DeleteAll(CancellationToken ct = default) => inner.DeleteAll(ct);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => inner.RemoveAll(strategy, ct);
        public Task EnsureReady(CancellationToken ct = default) => inner.EnsureReady(ct);
    }

    private class InterleavingBatch(IDataRepository<RaceDoc, string> adapter, IBatchSet<RaceDoc, string> inner)
        : IBatchSet<RaceDoc, string>
    {
        protected IBatchSet<RaceDoc, string> Inner => inner;
        public BatchExecutionCapabilities ExecutionCapabilities => inner.ExecutionCapabilities;
        public IBatchSet<RaceDoc, string> Add(RaceDoc entity) { inner.Add(entity); return this; }
        public IBatchSet<RaceDoc, string> Update(RaceDoc entity) { inner.Update(entity); return this; }
        public IBatchSet<RaceDoc, string> Update(string id, Action<RaceDoc> mutate) { inner.Update(id, mutate); return this; }
        public IBatchSet<RaceDoc, string> Delete(string id) { inner.Delete(id); return this; }
        public IBatchSet<RaceDoc, string> Clear() { inner.Clear(); return this; }

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            await Interleave(adapter, ct);
            return await inner.Save(options, ct);
        }
    }

    private sealed class GuardedInterleavingBatch(
        IDataRepository<RaceDoc, string> adapter,
        IBatchSet<RaceDoc, string> inner,
        IConditionalBatchDelete<RaceDoc, string> guarded)
        : InterleavingBatch(adapter, inner), IConditionalBatchDelete<RaceDoc, string>
    {
        public IBatchSet<RaceDoc, string> DeleteIf(string id, Filter guard) { guarded.DeleteIf(id, guard); return this; }
    }
}
