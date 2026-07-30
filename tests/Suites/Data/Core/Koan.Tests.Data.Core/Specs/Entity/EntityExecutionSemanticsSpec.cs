using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;
using Koan.Data.Core.Execution;
using Koan.Data.Core.Querying;
using System.Linq.Expressions;

namespace Koan.Tests.Data.Core.Specs.Entity;

public sealed class EntityExecutionSemanticsSpec
{
    [Fact]
    public async Task Get_many_normalizes_cardinality_order_duplicates_and_missing_slots()
    {
        var one = new ReceiptEntity { Id = "one", Value = "1" };
        var two = new ReceiptEntity { Id = "two", Value = "2" };
        var repository = new ReceiptRepository
        {
            GetManyResult = [two, one]
        };
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);

        var result = await facade.GetMany(["one", "missing", "two", "one"]);

        result.Should().HaveCount(4);
        result[0].Should().BeSameAs(one);
        result[1].Should().BeNull();
        result[2].Should().BeSameAs(two);
        result[3].Should().BeSameAs(one);
    }

    [Fact]
    public async Task Get_many_rejects_an_unrequested_identity()
    {
        var repository = new ReceiptRepository
        {
            GetManyResult = [new ReceiptEntity { Id = "other" }]
        };
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);

        var read = () => facade.GetMany(["requested"]);

        await read.Should().ThrowAsync<GetManyReceiptRejectedException>();
    }

    [Fact]
    public async Task Atomic_batch_rejects_before_deferred_load_or_native_save_without_exact_seam()
    {
        var repository = new ReceiptRepository(advertiseAtomic: true);
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);
        var batch = facade.CreateBatch().Update("missing", entity => entity.Value = "changed");

        var save = () => batch.Save(new BatchOptions(RequireAtomic: true));

        await save.Should().ThrowAsync<NotSupportedException>();
        repository.GetCalls.Should().Be(0);
        repository.Batch.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Deferred_mutation_missing_target_fails_before_native_save()
    {
        var repository = new ReceiptRepository();
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);

        var save = () => facade.CreateBatch()
            .Update("missing", entity => entity.Value = "changed")
            .Save();

        var failure = (await save.Should().ThrowAsync<BatchMutationTargetNotFoundException>()).Which;
        failure.OperationIndex.Should().Be(0);
        repository.Batch.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Atomic_batch_requires_and_returns_the_native_atomic_receipt()
    {
        var repository = new ReceiptRepository(advertiseAtomic: true);
        repository.Batch.Capabilities = BatchExecutionCapabilities.Atomic;
        repository.Batch.ResultAtomicity = BatchAtomicity.Atomic;
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);

        var result = await facade.CreateBatch()
            .Add(new ReceiptEntity { Id = "one" })
            .Save(new BatchOptions(RequireAtomic: true));

        result.Atomicity.Should().Be(BatchAtomicity.Atomic);
        repository.Batch.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task False_atomic_receipt_rejects_after_one_dispatch_without_replay()
    {
        var repository = new ReceiptRepository(advertiseAtomic: true);
        repository.Batch.Capabilities = BatchExecutionCapabilities.Atomic;
        repository.Batch.ResultAtomicity = BatchAtomicity.NotGuaranteed;
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);

        var save = () => facade.CreateBatch()
            .Add(new ReceiptEntity { Id = "one" })
            .Save(new BatchOptions(RequireAtomic: true));

        var failure = (await save.Should().ThrowAsync<BatchReceiptRejectedException>()).Which;
        failure.CommitOutcome.Should().Be(Koan.Data.Abstractions.Failures.DataCommitOutcome.Unknown);
        repository.Batch.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Batch_applies_the_same_timestamp_plan_as_single_upsert()
    {
        var repository = new ReceiptRepository();
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);
        var entity = new ReceiptEntity { Value = "new" };

        await facade.CreateBatch().Add(entity).Save();

        entity.Id.Should().NotBeNullOrWhiteSpace();
        entity.CreatedAt.Should().NotBe(default);
        entity.UpdatedAt.Should().NotBe(default);
        repository.Batch.Added.Should().ContainSingle().Which.Should().BeSameAs(entity);
    }

    [Fact]
    public async Task Lifecycle_bulk_upsert_prepares_all_then_uses_one_native_bulk_dispatch()
    {
        var before = 0;
        var after = 0;
        var lifecycle = new EntityLifecyclePlan<ReceiptEntity, string>();
        lifecycle.AddBeforeUpsert(context =>
        {
            before++;
            context.Current.Value += "-prepared";
            return ValueTask.FromResult(context.Proceed());
        });
        lifecycle.AddAfterUpsert(_ =>
        {
            after++;
            return ValueTask.CompletedTask;
        });
        var repository = new ReceiptRepository();
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository, lifecycle: lifecycle);
        var entities = new[]
        {
            new ReceiptEntity { Id = "one", Value = "a" },
            new ReceiptEntity { Id = "two", Value = "b" }
        };

        var count = await facade.UpsertMany(entities);

        count.Should().Be(2);
        before.Should().Be(2);
        after.Should().Be(2);
        repository.UpsertManyCalls.Should().Be(1);
        repository.UpsertCalls.Should().Be(0);
        entities.Select(entity => entity.Value).Should().Equal("a-prepared", "b-prepared");
    }

    [Fact]
    public async Task Inexact_bulk_receipt_is_unknown_and_never_replayed()
    {
        var repository = new ReceiptRepository { UpsertManyResult = 1 };
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);

        var save = () => facade.UpsertMany(
        [
            new ReceiptEntity { Id = "one" },
            new ReceiptEntity { Id = "two" }
        ]);

        var failure = (await save.Should().ThrowAsync<BulkMutationReceiptRejectedException>()).Which;
        failure.Expected.Should().Be(2);
        failure.Reported.Should().Be(1);
        failure.CommitOutcome.Should().Be(Koan.Data.Abstractions.Failures.DataCommitOutcome.Unknown);
        repository.UpsertManyCalls.Should().Be(1);
    }

    [Fact]
    public async Task Exact_upsert_outcome_is_capability_and_native_seam_coupled()
    {
        var repository = new ReceiptRepository(advertiseOutcomes: true)
        {
            UpsertOutcome = MutationOutcome.Inserted
        };
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);
        var entity = new ReceiptEntity { Id = "one", Value = "new" };

        var result = await ((IDataMutationOutcomes<ReceiptEntity, string>)facade)
            .UpsertWithOutcome(entity, default);

        result.Key.Should().Be("one");
        result.Outcome.Should().Be(MutationOutcome.Inserted);
        result.Entity.Should().BeSameAs(entity);
        result.CommitOutcome.Should().Be(Koan.Data.Abstractions.Failures.DataCommitOutcome.Committed);
    }

    [Fact]
    public async Task Delete_outcome_reports_missing_without_native_mutation()
    {
        var repository = new ReceiptRepository();
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);

        var result = await ((IDataMutationOutcomes<ReceiptEntity, string>)facade)
            .DeleteWithOutcome("missing", default);

        result.Outcome.Should().Be(MutationOutcome.Missing);
        result.CommitOutcome.Should().Be(Koan.Data.Abstractions.Failures.DataCommitOutcome.NotCommitted);
    }

    [Fact]
    public async Task Conditional_replace_requires_capability_and_native_seam_before_dispatch()
    {
        var unadvertised = new ReceiptRepository();
        var facade = new RepositoryFacade<ReceiptEntity, string>(unadvertised);

        var unsupported = () => facade.ConditionalReplaceAsync(
            new ReceiptEntity { Id = "one" }, entity => entity.Value == "prior");

        await unsupported.Should().ThrowAsync<NotSupportedException>();
        unadvertised.ConditionalCalls.Should().Be(0);

        var advertised = new ReceiptRepository(advertiseConditional: true)
        {
            ConditionalResult = false
        };
        facade = new RepositoryFacade<ReceiptEntity, string>(advertised);
        var lostRace = await facade.ConditionalReplaceAsync(
            new ReceiptEntity { Id = "one" }, entity => entity.Value == "prior");

        lostRace.Should().BeFalse();
        advertised.ConditionalCalls.Should().Be(1);
    }

    [Fact]
    public async Task Load_lifecycle_observes_only_the_final_visible_page()
    {
        var observed = new List<string>();
        var lifecycle = new EntityLifecyclePlan<ReceiptEntity, string>();
        lifecycle.AddAfterLoad(context =>
        {
            observed.Add(context.Current.Id);
            return ValueTask.CompletedTask;
        });
        var repository = new ReceiptRepository
        {
            QueryResult = new RepositoryQueryResult<ReceiptEntity>
            {
                Items =
                [
                    new ReceiptEntity { Id = "discarded", Value = "other" },
                    new ReceiptEntity { Id = "visible", Value = "keep" },
                    new ReceiptEntity { Id = "later", Value = "keep" }
                ]
            }
        };
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository, lifecycle: lifecycle);
        var boundary = (IDataQueryBoundary<ReceiptEntity, string>)facade;
        var requested = QueryDefinition.All
            .Where(Filter.Eq(nameof(ReceiptEntity.Value), "keep"))
            .WithPagination(1, 1);
        var (adapterQuery, residual) = FilterPushdownCoordinator.Plan(
            requested,
            FilterSupport.None,
            typeof(ReceiptEntity));

        var candidates = await boundary.QueryCandidates(adapterQuery);
        var finalized = FilterPushdownCoordinator.Finalize(requested, adapterQuery, residual, candidates);
        await boundary.MaterializeVisible(finalized.Page);

        finalized.Page.Should().ContainSingle().Which.Id.Should().Be("visible");
        observed.Should().Equal("visible");
    }

    [Fact]
    public async Task Complete_batch_outcomes_follow_logical_builder_order()
    {
        var repository = new ReceiptRepository();
        repository.Batch.Capabilities = BatchExecutionCapabilities.CompleteItemOutcomes;
        repository.Batch.SaveResult = new BatchResult(1, 0, 0)
        {
            HasCompleteItemOutcomes = true,
            Items =
            [
                new BatchItemResult(0, BatchOperation.Add, BatchItemOutcome.Applied),
                new BatchItemResult(1, BatchOperation.Update, BatchItemOutcome.Conflict),
                new BatchItemResult(2, BatchOperation.Delete, BatchItemOutcome.Missing)
            ]
        };
        var facade = new RepositoryFacade<ReceiptEntity, string>(repository);

        var result = await facade.CreateBatch()
            .Delete("gone")
            .Add(new ReceiptEntity { Id = "new" })
            .Update(new ReceiptEntity { Id = "existing" })
            .Save();

        result.Items.Select(item => (item.Index, item.Operation, item.Outcome)).Should().Equal(
            (0, BatchOperation.Delete, BatchItemOutcome.Missing),
            (1, BatchOperation.Add, BatchItemOutcome.Applied),
            (2, BatchOperation.Update, BatchItemOutcome.Conflict));
    }

    private sealed class ReceiptEntity : Entity<ReceiptEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        [Timestamp]
        public DateTimeOffset CreatedAt { get; set; }

        [Timestamp(OnSave = true)]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class ReceiptRepository(
        bool advertiseAtomic = false,
        bool advertiseOutcomes = false,
        bool advertiseConditional = false) :
        IDataRepository<ReceiptEntity, string>,
        IQueryRepository<ReceiptEntity, string>,
        IMutationOutcomeRepository<ReceiptEntity, string>,
        IConditionalWriteRepository<ReceiptEntity, string>,
        IDescribesCapabilities
    {
        public IReadOnlyList<ReceiptEntity?> GetManyResult { get; init; } = [];
        public int GetCalls { get; private set; }
        public int UpsertCalls { get; private set; }
        public int UpsertManyCalls { get; private set; }
        public int? UpsertManyResult { get; init; }
        public ReceiptBatch Batch { get; } = new();
        public MutationOutcome UpsertOutcome { get; init; } = MutationOutcome.Updated;
        public bool ConditionalResult { get; init; }
        public int ConditionalCalls { get; private set; }
        public RepositoryQueryResult<ReceiptEntity> QueryResult { get; init; } = new() { Items = [] };

        public void Describe(ICapabilities capabilities)
        {
            if (advertiseAtomic) capabilities.Add(DataCaps.Write.AtomicBatch);
            if (advertiseOutcomes) capabilities.Add(DataCaps.Write.MutationOutcomes);
            if (advertiseConditional) capabilities.Add(DataCaps.Write.ConditionalReplace);
        }

        public Task<ReceiptEntity?> Get(string id, CancellationToken ct = default)
        {
            GetCalls++;
            return Task.FromResult<ReceiptEntity?>(null);
        }

        public Task<IReadOnlyList<ReceiptEntity?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default)
            => Task.FromResult(GetManyResult);

        public Task<ReceiptEntity> Upsert(ReceiptEntity model, CancellationToken ct = default)
        {
            UpsertCalls++;
            return Task.FromResult(model);
        }

        public Task<MutationResult<ReceiptEntity, string>> UpsertWithOutcome(
            ReceiptEntity model,
            CancellationToken ct = default)
            => Task.FromResult(new MutationResult<ReceiptEntity, string>(
                model.Id,
                UpsertOutcome,
                model,
                Koan.Data.Abstractions.Failures.DataCommitOutcome.Committed));

        public Task<bool> ConditionalReplaceAsync(
            ReceiptEntity model,
            Expression<Func<ReceiptEntity, bool>> guard,
            CancellationToken ct = default)
        {
            ConditionalCalls++;
            return Task.FromResult(ConditionalResult);
        }

        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(false);

        public Task<int> UpsertMany(IEnumerable<ReceiptEntity> models, CancellationToken ct = default)
        {
            UpsertManyCalls++;
            return Task.FromResult(UpsertManyResult ?? models.Count());
        }

        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default)
            => Task.FromResult(ids.Count());

        public Task<RepositoryQueryResult<ReceiptEntity>> Query(
            QueryDefinition query,
            CancellationToken ct = default)
            => Task.FromResult(QueryResult);

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
            => Task.FromResult(CountResult.Exact(QueryResult.Items.Count));

        public Task<int> DeleteAll(CancellationToken ct = default) => Task.FromResult(0);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => Task.FromResult(0L);
        public IBatchSet<ReceiptEntity, string> CreateBatch() => Batch;
    }

    private sealed class ReceiptBatch : IBatchSet<ReceiptEntity, string>
    {
        public List<ReceiptEntity> Added { get; } = [];
        public List<ReceiptEntity> Updated { get; } = [];
        public List<string> Deleted { get; } = [];
        public int SaveCalls { get; private set; }
        public BatchExecutionCapabilities Capabilities { get; set; }
        public BatchAtomicity ResultAtomicity { get; set; }
        public BatchResult? SaveResult { get; set; }

        public BatchExecutionCapabilities ExecutionCapabilities => Capabilities;

        public IBatchSet<ReceiptEntity, string> Add(ReceiptEntity entity) { Added.Add(entity); return this; }
        public IBatchSet<ReceiptEntity, string> Update(ReceiptEntity entity) { Updated.Add(entity); return this; }
        public IBatchSet<ReceiptEntity, string> Update(string id, Action<ReceiptEntity> mutate) => this;
        public IBatchSet<ReceiptEntity, string> Delete(string id) { Deleted.Add(id); return this; }
        public IBatchSet<ReceiptEntity, string> Clear() { Added.Clear(); Updated.Clear(); Deleted.Clear(); return this; }

        public Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            SaveCalls++;
            return Task.FromResult(SaveResult ?? new BatchResult(Added.Count, Updated.Count, Deleted.Count)
            {
                Atomicity = ResultAtomicity
            });
        }
    }
}
