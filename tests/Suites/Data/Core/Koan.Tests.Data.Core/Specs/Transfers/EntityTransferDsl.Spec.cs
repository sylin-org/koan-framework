using System.Collections.Concurrent;
using System.Collections.Frozen;
using Koan.Core.Capabilities;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transfers;
using Koan.Data.Core.Querying;
using Koan.Tests.Data.Core.Support;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Transfers;

public sealed class EntityTransferDslSpec
{
    private static readonly string[] Partitions =
    [
        "active",
        "inactive",
        "hot",
        "archive",
        "batch",
        "dest",
        "mirror",
        "filtered",
        "sync",
        "sync-target",
        "reporting",
        "secondary"
    ];

    private readonly ITestOutputHelper _output;

    public EntityTransferDslSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Copy_ToPartition_CopiesFilteredEntities()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("active"))
        {
            await new TransferTodo { Title = "A", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            await new TransferTodo { Title = "B", Active = false, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var audits = new List<TransferAuditBatch>();
        var result = await TransferTodo.Copy(p => p.Active)
            .From(partition: "active")
            .To(partition: "inactive")
            .Audit(audits.Add)
            .Run();

        result.Kind.Should().Be(TransferKind.Copy);
        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(0);
        result.ReadCount.Should().Be(1);
        result.Warnings.Should().BeEmpty();
        audits.Should().NotBeEmpty();
        audits.Last().IsSummary.Should().BeTrue();

        using (runtime.UsePartition("inactive"))
        {
            var items = await TransferTodo.All();
            items.Should().ContainSingle(x => x.Title == "A");
        }

        using (runtime.UsePartition("active"))
        {
            var items = await TransferTodo.All();
            items.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Copy_Predicate_AppliesFilter()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        await new TransferTodo { Title = "keep", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        await new TransferTodo { Title = "drop", Active = true, UpdatedAt = DateTime.UtcNow }.Save();

        var result = await TransferTodo.Copy(todo => todo.Title == "keep")
            .To(partition: "filtered")
            .Run();

        result.CopiedCount.Should().Be(1);

        using (runtime.UsePartition("filtered"))
        {
            var items = await TransferTodo.All();
            items.Should().ContainSingle(t => t.Title == "keep");
        }
    }

    [Fact]
    public async Task Move_DefaultStrategy_RemovesFromSource()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("hot"))
        {
            for (var i = 0; i < 3; i++)
            {
                await new TransferTodo { Title = $"Item {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move(p => true)
            .From(partition: "hot")
            .To(partition: "archive")
            .Run();

        result.Kind.Should().Be(TransferKind.Move);
        result.CopiedCount.Should().Be(3);
        result.DeletedCount.Should().Be(3);

        using (runtime.UsePartition("hot"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }

        using (runtime.UsePartition("archive"))
        {
            (await TransferTodo.All()).Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task Move_BatchedStrategy_RespectsBatching()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("batch"))
        {
            for (var i = 0; i < 4; i++)
            {
                await new TransferTodo { Title = $"Batch {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move()
            .From(partition: "batch")
            .To(partition: "dest")
            .Batch(1)
            .Run();

        result.CopiedCount.Should().Be(4);
        result.DeletedCount.Should().Be(4);

        using (runtime.UsePartition("batch"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Move_SyncedStrategy_RemovesAsItGoes()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("sync"))
        {
            for (var i = 0; i < 2; i++)
            {
                await new TransferTodo { Title = $"Sync {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move()
            .From(partition: "sync")
            .To(partition: "sync-target")
            .Batch(1)
            .Run();

        result.CopiedCount.Should().Be(2);
        result.DeletedCount.Should().Be(2);

        using (runtime.UsePartition("sync"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Mirror_Push_SynchronizesToTarget()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        await new TransferTodo { Title = "primary", Active = true, UpdatedAt = DateTime.UtcNow }.Save();

        var result = await TransferTodo.Mirror()
            .To(partition: "mirror")
            .Run();

        result.Kind.Should().Be(TransferKind.Mirror);
        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(0);

        using (runtime.UsePartition("mirror"))
        {
            (await TransferTodo.All()).Should().ContainSingle(x => x.Title == "primary");
        }
    }

    [Fact]
    public async Task Mirror_Pull_SynchronizesBackToDefault()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("mirror"))
        {
            await new TransferTodo { Title = "remote", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var result = await TransferTodo.Mirror(mode: MirrorMode.Pull)
            .To(partition: "mirror")
            .Run();

        result.CopiedCount.Should().Be(1);

        var all = await TransferTodo.All();
        all.Should().ContainSingle(x => x.Title == "remote");
    }

    [Fact]
    public async Task Mirror_Bidirectional_UsesTimestampForResolution()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        var primary = await new TransferTodo { Title = "v1", Active = true, UpdatedAt = DateTime.UtcNow.AddMinutes(-2) }.Save();

        using (runtime.UsePartition("reporting"))
        {
            await new TransferTodo { Id = primary.Id, Title = "v2", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var result = await TransferTodo.Mirror(mode: MirrorMode.Bidirectional)
            .To(partition: "reporting")
            .Run();

        result.Conflicts.Should().BeEmpty();

        var updated = await TransferTodo.Get(primary.Id);
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("v2");

        using (runtime.UsePartition("reporting"))
        {
            var target = await TransferTodo.Get(primary.Id);
            target.Should().NotBeNull();
            target!.Title.Should().Be("v2");
        }
    }

    [Fact]
    public async Task Mirror_Bidirectional_WithoutTimestamp_ReportsConflicts()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        runtime.BindHost();

        var baseNote = await new BasicNote { Content = "default" }.Save();

        using (runtime.UsePartition("secondary"))
        {
            await new BasicNote { Id = baseNote.Id, Content = "secondary" }.Save();
        }

        var result = await BasicNote.Mirror(mode: MirrorMode.Bidirectional)
            .To(partition: "secondary")
            .Run();

        result.Conflicts.Should().NotBeEmpty();
        result.CopiedCount.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("No [Timestamp]"));

        var defaultNote = await BasicNote.Get(baseNote.Id);
        defaultNote.Should().NotBeNull();
        defaultNote!.Content.Should().Be("default");
    }

    [Fact]
    public async Task To_WithSourceAndAdapter_ShouldThrow()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);

        Action act = () => TransferTodo.Copy().To(source: "primary", adapter: "sqlite");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Batch_BoundsProviderPagesAndDestinationWrites()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("active"))
            for (var i = 0; i < 5; i++)
                await new TransferTodo { Title = $"bounded-{i}", Active = true }.Save();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        var audits = new List<TransferAuditBatch>();

        var result = await TransferTodo.Copy()
            .From(partition: "active")
            .To(partition: "archive")
            .Batch(2)
            .Audit(audits.Add)
            .Run();

        result.CopiedCount.Should().Be(5);
        repository.MaxRequestedPageSize.Should().Be(2);
        repository.MaxUpsertManyCount.Should().Be(2);
        audits.Where(batch => !batch.IsSummary).Should().OnlyContain(batch => batch.BatchCount <= 2);
        audits.Last().IsSummary.Should().BeTrue();
    }

    [Fact]
    public async Task Copy_AmbiguousDestinationFault_IsNotReplayed()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("active"))
            await new TransferTodo { Title = "one-dispatch", Active = true }.Save();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        repository.ThrowAfterNextUpsertMany = true;

        var act = () => TransferTodo.Copy()
            .From(partition: "active")
            .To(partition: "archive")
            .Batch(1)
            .Run();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("injected ambiguous destination fault");
        repository.UpsertManyCalls.Should().Be(1);
    }

    /// <summary>
    /// DATA-0113. This spec previously asserted that a transfer over an adapter without provider-bounded
    /// paging throws <c>QueryStreamRejectedException</c>. That contradicted the real-adapter conformance
    /// kit — which asserts transfers work and had been red for InMemory, JSON, and Redis ever since — and
    /// it left <c>Copy</c>/<c>Move</c> broken on JSON, the Data pillar's own floor adapter. The transfer
    /// DSL now reads with the strongest strategy the provider supports, and says which one it used.
    /// </summary>
    [Fact]
    public async Task Copy_MissingBoundedPaging_MaterializesTheSourceAndReportsIt()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        using (runtime.UsePartition("active"))
            for (var i = 0; i < 3; i++)
                await new TransferTodo { Title = $"unbounded-{i}", Active = true }.Save();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        repository.AdvertiseBoundedPaging = false;

        var result = await TransferTodo.Copy()
            .From(partition: "active")
            .To(partition: "archive")
            .Batch(2)
            .Run();

        result.CopiedCount.Should().Be(3, "an unqualified provider must not silently drop the transfer");
        result.Warnings.Should().Contain(
            warning => warning.Contains("provider-bounded paging"),
            "materializing instead of streaming is reported, never silent");

        // Writes stay batched even when the read could not be: 3 rows at Batch(2) is two dispatches.
        repository.UpsertManyCalls.Should().Be(2);
    }

    [Fact]
    public async Task Copy_CancellationBeforeCandidatePage_PreventsDestinationDispatch()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        using var cancellation = new CancellationTokenSource();
        repository.BeforeNextQuery = cancellation.Cancel;

        var act = () => TransferTodo.Copy()
            .To(partition: "archive")
            .Batch(2)
            .Run(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        repository.QueryCalls.Should().Be(1);
        repository.UpsertManyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Copy_InvalidPageReceipt_FailsBeforeDestinationDispatch()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();
        repository.HandlePagination = false;

        var act = () => TransferTodo.Copy()
            .To(partition: "archive")
            .Batch(2)
            .Run();

        await act.Should().ThrowAsync<QueryStreamRejectedException>();
        repository.QueryCalls.Should().Be(1);
        repository.UpsertManyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Copy_SameContext_IsNoOpBeforeProviderWork()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        var repository = runtime.Services.GetRequiredService<TransferAdapterFactory>()
            .Repository<TransferTodo>();
        repository.ResetObservations();

        var result = await TransferTodo.Copy()
            .From(partition: "active")
            .To(partition: "active")
            .Run();

        result.CopiedCount.Should().Be(0);
        repository.QueryCalls.Should().Be(0);
        repository.UpsertManyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Mirror_Push_RemovesReplicaOnlyIdentitiesAfterBoundedRead()
    {
        await using var runtime = await CreateRuntime();
        await Reset(runtime);
        runtime.BindHost();

        TransferTodo current;
        using (runtime.UsePartition("active"))
            current = await new TransferTodo { Title = "current", Active = true }.Save();
        using (runtime.UsePartition("archive"))
        {
            await new TransferTodo { Id = current.Id, Title = "old", Active = true }.Save();
            await new TransferTodo { Title = "replica-only", Active = true }.Save();
        }

        var result = await TransferTodo.Mirror()
            .From(partition: "active")
            .To(partition: "archive")
            .Batch(1)
            .Run();

        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(1);
        using (runtime.UsePartition("archive"))
            (await TransferTodo.All()).Should().ContainSingle(todo => todo.Id == current.Id && todo.Title == "current");
    }

    private static Task<DataCoreRuntimeFixture> CreateRuntime()
        => DataCoreRuntimeFixture.CreateAsync(configureServices: services =>
        {
            services.AddSingleton<TransferAdapterFactory>();
            services.AddSingleton<IDataAdapterFactory>(provider =>
                provider.GetRequiredService<TransferAdapterFactory>());
        });

    private static async ValueTask Reset(DataCoreRuntimeFixture runtime)
    {
        runtime.ResetEntityCaches();

        await TransferTodo.RemoveAll();
        await BasicNote.RemoveAll();

        foreach (var partition in Partitions)
        {
            using var lease = runtime.UsePartition(partition);
            await TransferTodo.RemoveAll();
            await BasicNote.RemoveAll();
        }
    }

    [DataAdapter(TransferAdapterFactory.ProviderId)]
    private sealed class TransferTodo : Entity<TransferTodo>
    {
        public string Title { get; set; } = "";
        public bool Active { get; set; }

        [Timestamp(OnSave = true)]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    [DataAdapter(TransferAdapterFactory.ProviderId)]
    private sealed class BasicNote : Entity<BasicNote>
    {
        public string Content { get; set; } = "";
    }

    private sealed class TransferAdapterFactory : IDataAdapterFactory
    {
        public const string ProviderId = "transfer-spec";
        private readonly ConcurrentDictionary<(Type Entity, string Source), object> _repositories = new();

        public string Provider => ProviderId;

        public TransferRepository<TEntity> Repository<TEntity>(string source = "Default")
            where TEntity : class, IEntity<string>
            => (TransferRepository<TEntity>)_repositories.GetOrAdd(
                (typeof(TEntity), source),
                static key => Activator.CreateInstance(typeof(TransferRepository<>).MakeGenericType(key.Entity))!);

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
            IServiceProvider services,
            string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
        {
            if (typeof(TKey) != typeof(string) ||
                (typeof(TEntity) != typeof(TransferTodo) && typeof(TEntity) != typeof(BasicNote)))
                throw new InvalidOperationException($"The transfer spec adapter cannot create {typeof(TEntity).Name}<{typeof(TKey).Name}>.");

            var repository = _repositories.GetOrAdd(
                (typeof(TEntity), source),
                static key => Activator.CreateInstance(typeof(TransferRepository<>).MakeGenericType(key.Entity))!);
            return (IDataRepository<TEntity, TKey>)repository;
        }

        public StorageNamingCapability GetNamingCapability(IServiceProvider services)
            => new()
            {
                Style = StorageNamingStyle.EntityType,
                Casing = NameCasing.AsIs,
                PartitionSeparator = '#',
                Partition = PartitionTokenPolicy.Default
            };
    }

    private sealed class TransferRepository<TEntity> :
        IDataRepository<TEntity, string>,
        IQueryRepository<TEntity, string>,
        IDescribesCapabilities
        where TEntity : class, IEntity<string>
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TEntity>> _partitions =
            new(StringComparer.Ordinal);

        public bool AdvertiseBoundedPaging { get; set; } = true;
        public bool HandlePagination { get; set; } = true;
        public bool ThrowAfterNextUpsertMany { get; set; }
        public Action? BeforeNextQuery { get; set; }
        public int QueryCalls { get; private set; }
        public int UpsertManyCalls { get; private set; }
        public int MaxRequestedPageSize { get; private set; }
        public int MaxUpsertManyCount { get; private set; }

        public void ResetObservations()
        {
            AdvertiseBoundedPaging = true;
            HandlePagination = true;
            ThrowAfterNextUpsertMany = false;
            BeforeNextQuery = null;
            QueryCalls = 0;
            UpsertManyCalls = 0;
            MaxRequestedPageSize = 0;
            MaxUpsertManyCount = 0;
        }

        public void Describe(ICapabilities capabilities)
        {
            capabilities
                .Add(DataCaps.Query.Linq)
                .Add(DataCaps.Query.Filter, FilterSupport.Full);
            if (AdvertiseBoundedPaging)
                capabilities.Add(DataCaps.Query.ProviderBoundedPaging);
        }

        public Task<TEntity?> Get(string id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Current().GetValueOrDefault(id));
        }

        public Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var set = Current();
            return Task.FromResult<IReadOnlyList<TEntity?>>(ids.Select(set.GetValueOrDefault).ToArray());
        }

        public Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Current()[model.Id] = model;
            return Task.FromResult(model);
        }

        public Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
        {
            UpsertManyCalls++;
            var count = 0;
            var set = Current();
            foreach (var model in models)
            {
                ct.ThrowIfCancellationRequested();
                set[model.Id] = model;
                count++;
            }
            MaxUpsertManyCount = Math.Max(MaxUpsertManyCount, count);
            if (ThrowAfterNextUpsertMany)
            {
                ThrowAfterNextUpsertMany = false;
                throw new InvalidOperationException("injected ambiguous destination fault");
            }
            return Task.FromResult(count);
        }

        public Task<bool> Delete(string id, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Current().TryRemove(id, out _));
        }

        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var count = 0;
            var set = Current();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                if (set.TryRemove(id, out _)) count++;
            }
            return Task.FromResult(count);
        }

        public Task<int> DeleteAll(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var set = Current();
            var count = set.Count;
            set.Clear();
            return Task.FromResult(count);
        }

        public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
            => await DeleteAll(ct);

        public Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            QueryCalls++;
            MaxRequestedPageSize = Math.Max(MaxRequestedPageSize, query.HasPagination ? query.EffectivePageSize() : 0);
            var before = BeforeNextQuery;
            BeforeNextQuery = null;
            before?.Invoke();
            ct.ThrowIfCancellationRequested();
            IEnumerable<TEntity> selected = Current().Values;
            if (query.Filter is not null)
                selected = selected.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));

            var filtered = selected.OrderBy(entity => entity.Id, StringComparer.Ordinal).ToArray();
            long? total = query.CountStrategy is null ? null : filtered.Length;
            IEnumerable<TEntity> page = filtered;
            if (query.HasPagination)
                page = page.Skip(query.EffectiveOffset()).Take(query.EffectivePageSize());

            return Task.FromResult(new RepositoryQueryResult<TEntity>
            {
                Items = page.ToArray(),
                FilterHandled = query.Filter is not null,
                TotalCount = total,
                CountExecution = total is null ? CountExecutionKind.None : CountExecutionKind.Exact,
                PaginationHandled = HandlePagination && query.HasPagination,
                SortHandled = query.Sort.ToFrozenSet()
            });
        }

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<TEntity> selected = Current().Values;
            if (query.Filter is not null)
                selected = selected.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));
            return Task.FromResult(CountResult.Exact(selected.LongCount()));
        }

        public IBatchSet<TEntity, string> CreateBatch() => throw new NotSupportedException();

        private ConcurrentDictionary<string, TEntity> Current()
            => _partitions.GetOrAdd(EntityContext.Current?.Partition ?? "", static _ => new(StringComparer.Ordinal));
    }
}
