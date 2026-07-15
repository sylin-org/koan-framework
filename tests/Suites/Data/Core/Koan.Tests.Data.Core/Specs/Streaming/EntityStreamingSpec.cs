using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Core.Context;
using Koan.Core.Diagnostics;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Model;
using Koan.Data.Core.Querying;
using Koan.Data.Core.Infrastructure;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Streaming;

[Collection(nameof(EntityStreamingSpec))]
[CollectionDefinition(nameof(EntityStreamingSpec), DisableParallelization = true)]
public sealed class EntityStreamingSpec : IAsyncLifetime
{
    private readonly FakeStreamingRepository _repository = new();
    private IntegrationHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .WithSetting("Koan:Data:Sources:stream_a:Adapter", FakeStreamingAdapterFactory.ProviderId)
            .WithSetting("Koan:Data:Sources:stream_b:Adapter", FakeStreamingAdapterFactory.ProviderId)
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory>(new FakeStreamingAdapterFactory(_repository));
                services.AddSingleton<IKoanContextCarrier, StreamingAxisCarrier>();
            })
            .StartAsync(TestContext.Current.CancellationToken);

        AppHost.Current = _host.Services;
        TestHooks.ResetDataConfigs();
    }

    public async ValueTask DisposeAsync()
    {
        if (ReferenceEquals(AppHost.Current, _host.Services))
            AppHost.Current = null;

        TestHooks.ResetDataConfigs();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Missing_capability_rejects_before_query()
    {
        _repository.Reset(Rows(1, 2, 3), advertiseBoundedPaging: false);

        var exception = await Reject(StreamingRecord.AllStream(batchSize: 2));

        AssertCorrective(exception, 2);
        _repository.QueryCalls.Should().BeEmpty();
        _repository.CountCalls.Should().Be(0);

        var fact = StreamFact();
        fact.State.Should().Be(KoanFactState.Rejected);
        fact.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.MissingProviderBoundedPaging);
        fact.Correction.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_batch_size_rejects_before_query(int batchSize)
    {
        _repository.Reset(Rows(1, 2, 3));

        var exception = await Reject(StreamingRecord.AllStream(batchSize));

        AssertCorrective(exception, batchSize);
        _repository.QueryCalls.Should().BeEmpty();
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Streams_exact_candidate_pages_without_counting()
    {
        _repository.Reset(Rows(1, 2, 3, 4, 5));

        var result = await Collect(StreamingRecord.AllStream(batchSize: 2));

        result.Select(record => record.Sequence).Should().Equal(1, 2, 3, 4, 5);
        _repository.QueryCalls.Select(query => query.EffectivePage()).Should().Equal(1, 2, 3);
        _repository.QueryCalls.Select(query => query.EffectivePageSize()).Should().OnlyContain(size => size == 2);
        _repository.QueryCalls.Should().OnlyContain(query => query.CountStrategy == null);
        _repository.QueryCalls.Should().OnlyContain(query =>
            query.Sort.Any(spec => string.Equals(spec.Path.DotPath, nameof(StreamingRecord.Id), StringComparison.Ordinal)));
        _repository.CountCalls.Should().Be(0);

        var fact = StreamFact();
        fact.State.Should().Be(KoanFactState.Selected);
        fact.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.ProviderBoundedPaging);
        fact.Summary.Should().Contain(nameof(StreamingRecord)).And.Contain("2");
    }

    [Fact]
    public async Task First_yield_does_not_request_a_later_page()
    {
        _repository.Reset(Rows(1, 2, 3, 4));
        var enumerator = StreamingRecord.AllStream(batchSize: 2).GetAsyncEnumerator();

        try
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            enumerator.Current.Sequence.Should().Be(1);
            _repository.QueryCalls.Should().ContainSingle()
                .Which.EffectivePage().Should().Be(1);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Early_dispose_does_not_fetch_another_page()
    {
        _repository.Reset(Rows(1, 2, 3, 4));
        var enumerator = StreamingRecord.AllStream(batchSize: 2).GetAsyncEnumerator();

        (await enumerator.MoveNextAsync()).Should().BeTrue();
        await enumerator.DisposeAsync();

        _repository.QueryCalls.Should().ContainSingle()
            .Which.EffectivePage().Should().Be(1);
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Empty_residual_page_continues_to_the_next_candidate_page()
    {
        _repository.Reset(
            [Record(1, include: false), Record(2, include: false), Record(3, include: true)],
            filterSupport: FilterSupport.None);

        var result = await Collect(StreamingRecord.QueryStream(record => record.Include, batchSize: 2));

        result.Select(record => record.Sequence).Should().Equal(3);
        _repository.QueryCalls.Select(query => query.EffectivePage()).Should().Equal(1, 2);
        _repository.QueryCalls.Should().OnlyContain(query => query.Filter == null);
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Provider_that_does_not_handle_pagination_rejects_before_page_yield()
    {
        _repository.Reset(Rows(1, 2, 3), paginationHandled: false);

        var exception = await Reject(StreamingRecord.AllStream(batchSize: 2));

        AssertCorrective(exception, 2);
        _repository.QueryCalls.Should().ContainSingle();
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Provider_that_returns_too_many_candidates_rejects_before_page_yield()
    {
        _repository.Reset(Rows(1, 2, 3, 4), returnOneExtraCandidate: true);

        var exception = await Reject(StreamingRecord.AllStream(batchSize: 2));

        AssertCorrective(exception, 2);
        _repository.QueryCalls.Should().ContainSingle();
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Provider_that_does_not_handle_sort_rejects_before_page_yield()
    {
        _repository.Reset(Rows(1, 2, 3), sortHandled: false);

        var exception = await Reject(StreamingRecord.AllStream(nameof(StreamingRecord.Sequence), batchSize: 2));

        AssertCorrective(exception, 2);
        _repository.QueryCalls.Should().ContainSingle();
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Lowercase_id_member_does_not_replace_the_entity_key_tiebreaker()
    {
        _repository.Reset(Rows(1, 2, 3));

        _ = await Collect(StreamingRecord.AllStream(
            sort => sort.OrderBy(record => record.id),
            batchSize: 2));

        _repository.QueryCalls.Should().NotBeEmpty();
        _repository.QueryCalls[0].Sort.Select(spec => spec.Path.DotPath)
            .Should().Equal(nameof(StreamingRecord.id), nameof(StreamingRecord.Id));
    }

    [Fact]
    public async Task Caller_requested_entity_id_order_rejects_before_provider_io()
    {
        _repository.Reset(Rows(1, 2, 3));

        var exception = await Reject(StreamingRecord.AllStream(
            sort => sort.OrderBy(record => record.Id),
            batchSize: 2));

        exception.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.UnsupportedStreamSort);
        _repository.QueryCalls.Should().BeEmpty();
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Custom_entity_key_rejects_before_provider_io()
    {
        var repository = new CustomKeyRepository();
        var stream = QueryStreamCoordinator.Execute<CustomKeyRecord, int>(
            repository,
            QueryDefinition.All,
            "custom-key-spec",
            requestedBatchSize: 2,
            facts: null,
            enterContext: static () => EmptyScope.Instance);

        Func<Task> enumerate = async () => _ = await Collect(stream);
        var exception = (await enumerate.Should().ThrowAsync<QueryStreamRejectedException>()).Which;

        exception.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.UnsupportedStreamSort);
        exception.EntityType.Should().Contain(nameof(CustomKeyRecord));
        repository.QueryCalls.Should().Be(0);
    }

    [Fact]
    public void Filter_field_resolution_prefers_exact_case_for_Id_and_lowercase_id()
    {
        var key = FieldPathResolver.Resolve(typeof(StreamingRecord), FieldPath.Of(nameof(StreamingRecord.Id)));
        var business = FieldPathResolver.Resolve(typeof(StreamingRecord), FieldPath.Of(nameof(StreamingRecord.id)));
        Action ambiguous = () => FieldPathResolver.Resolve(typeof(StreamingRecord), FieldPath.Of("iD"));

        key.Members.Single().Name.Should().Be(nameof(StreamingRecord.Id));
        business.Members.Single().Name.Should().Be(nameof(StreamingRecord.id));
        ambiguous.Should().Throw<InvalidFilterFieldException>();
    }

    [Fact]
    public async Task Complex_top_level_sort_rejects_before_provider_io()
    {
        _repository.Reset(Rows(1, 2, 3));

        var exception = await Reject(StreamingRecord.AllStream(
            sort => sort.OrderBy(record => record.Detail),
            batchSize: 2));

        exception.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.UnsupportedStreamSort);
        _repository.QueryCalls.Should().BeEmpty();
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public async Task Scalars_without_a_portable_cross_adapter_order_reject_before_provider_io()
    {
        _repository.Reset(Rows(1, 2, 3));

        var decimalRejection = await Reject(StreamingRecord.AllStream(
            sort => sort.OrderBy(record => record.Amount),
            batchSize: 2));
        var dateTimeRejection = await Reject(StreamingRecord.AllStream(
            sort => sort.OrderBy(record => record.ObservedAt),
            batchSize: 2));
        var stringRejection = await Reject(StreamingRecord.AllStream(
            sort => sort.OrderBy(record => record.Title),
            batchSize: 2));
        var nullableRejection = await Reject(StreamingRecord.AllStream(
            sort => sort.OrderBy(record => record.OptionalSequence),
            batchSize: 2));
        var wideIntegerRejection = await Reject(StreamingRecord.AllStream(
            sort => sort.OrderBy(record => record.Revision),
            batchSize: 2));

        decimalRejection.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.UnsupportedStreamSort);
        dateTimeRejection.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.UnsupportedStreamSort);
        stringRejection.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.UnsupportedStreamSort);
        nullableRejection.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.UnsupportedStreamSort);
        wideIntegerRejection.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.UnsupportedStreamSort);
        _repository.QueryCalls.Should().BeEmpty();
        _repository.CountCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(typeof(bool), true)]
    [InlineData(typeof(byte), true)]
    [InlineData(typeof(sbyte), true)]
    [InlineData(typeof(short), true)]
    [InlineData(typeof(ushort), true)]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(int?), false)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(char), false)]
    [InlineData(typeof(uint), false)]
    [InlineData(typeof(long), false)]
    [InlineData(typeof(ulong), false)]
    [InlineData(typeof(decimal), false)]
    [InlineData(typeof(float), false)]
    [InlineData(typeof(double), false)]
    [InlineData(typeof(DateTime), false)]
    [InlineData(typeof(DateTimeOffset), false)]
    [InlineData(typeof(DateOnly), false)]
    [InlineData(typeof(TimeOnly), false)]
    [InlineData(typeof(TimeSpan), false)]
    [InlineData(typeof(Guid), false)]
    public void Portable_stream_sort_floor_is_exact(Type type, bool expected)
    {
        TypeClassification.IsPortableStreamSortScalar(type).Should().Be(expected);
        QueryStreamCoordinator.IsProviderStableIdentifierType(type).Should().Be(type == typeof(string));
    }

    [Fact]
    public void Pagination_rejects_an_offset_that_exceeds_the_provider_contract()
    {
        Action fluent = () => QueryDefinition.All.WithPagination(int.MaxValue, 2);
        Action direct = () => _ = new QueryDefinition { Page = int.MaxValue, PageSize = 2 }.EffectiveOffset();

        fluent.Should().Throw<ArgumentOutOfRangeException>();
        direct.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Cancellation_is_checked_between_items_without_fetching_another_page()
    {
        _repository.Reset(Rows(1, 2, 3));
        using var cancellation = new CancellationTokenSource();
        var enumerator = StreamingRecord.AllStream(batchSize: 2, ct: cancellation.Token).GetAsyncEnumerator();

        try
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            cancellation.Cancel();

            Func<Task> moveNext = async () => _ = await enumerator.MoveNextAsync();
            await moveNext.Should().ThrowAsync<OperationCanceledException>();
            _repository.QueryCalls.Should().ContainSingle();
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Cancellation_token_overloads_reject_before_query_when_pre_cancelled()
    {
        _repository.Reset(Rows(1, 2, 3));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> all = () => Collect(StreamingRecord.AllStream(cancellation.Token));
        Func<Task> query = () => Collect(StreamingRecord.QueryStream(record => record.Include, cancellation.Token));

        await all.Should().ThrowAsync<OperationCanceledException>();
        await query.Should().ThrowAsync<OperationCanceledException>();
        _repository.QueryCalls.Should().BeEmpty();
        _repository.CountCalls.Should().Be(0);
    }

    [Fact]
    public void Default_literal_calls_remain_source_compatible()
    {
        IAsyncEnumerable<StreamingRecord> all = StreamingRecord.AllStream(default);
        IAsyncEnumerable<StreamingRecord> query = StreamingRecord.QueryStream(
            static record => record.Include, default);

        all.Should().NotBeNull();
        query.Should().NotBeNull();
    }

    [Fact]
    public async Task Enumeration_keeps_its_initial_routing_and_registered_context_across_pages()
    {
        _repository.Reset(Rows(1, 2, 3, 4));
        IAsyncEnumerator<StreamingRecord> enumerator;

        using (EntityContext.With(source: "stream_a", partition: "stream-a"))
        using (KoanContext.Push(new StreamingAxis("axis-a")))
        {
            enumerator = StreamingRecord.AllStream(batchSize: 2).GetAsyncEnumerator();
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            (await enumerator.MoveNextAsync()).Should().BeTrue();
        }

        try
        {
            using (EntityContext.With(source: "stream_b", partition: "stream-b"))
            using (KoanContext.Push(new StreamingAxis("axis-b")))
                (await enumerator.MoveNextAsync()).Should().BeTrue();

            _repository.ObservedPartitions.Should().Equal("stream-a", "stream-a");
            _repository.ObservedSources.Should().Equal("stream_a", "stream_a");
            _repository.ObservedAxes.Should().Equal("axis-a", "axis-a");
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static async Task<QueryStreamRejectedException> Reject(IAsyncEnumerable<StreamingRecord> stream)
    {
        Func<Task> enumerate = async () => _ = await Collect(stream);
        return (await enumerate.Should().ThrowAsync<QueryStreamRejectedException>()).Which;
    }

    private static void AssertCorrective(QueryStreamRejectedException exception, int batchSize)
    {
        exception.EntityType.Should().Contain(nameof(StreamingRecord));
        exception.Provider.Should().Be(FakeStreamingAdapterFactory.ProviderId);
        exception.ReasonCode.Should().NotBeNullOrWhiteSpace();
        exception.Correction.Should().NotBeNullOrWhiteSpace();
        exception.BatchSize.Should().Be(batchSize);
    }

    private static async Task<List<TEntity>> Collect<TEntity>(IAsyncEnumerable<TEntity> stream)
    {
        var result = new List<TEntity>();
        await foreach (var item in stream)
            result.Add(item);
        return result;
    }

    private KoanFact StreamFact()
        => _host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts.Single(fact =>
            fact.Code == Constants.Diagnostics.Codes.StreamExecution
            && fact.Subject.EndsWith(nameof(StreamingRecord), StringComparison.Ordinal));

    private static IReadOnlyList<StreamingRecord> Rows(params int[] sequences)
        => sequences.Select(sequence => Record(sequence, include: true)).ToList();

    private static StreamingRecord Record(int sequence, bool include)
        => new()
        {
            Id = sequence.ToString("D4"),
            id = sequence % 2,
            Sequence = sequence,
            Include = include
        };

    [DataAdapter(FakeStreamingAdapterFactory.ProviderId)]
    private sealed class StreamingRecord : Entity<StreamingRecord>
    {
        public int id { get; set; }
        public int Sequence { get; set; }
        public bool Include { get; set; }
        public string Title { get; set; } = "";
        public int? OptionalSequence { get; set; }
        public long Revision { get; set; }
        public decimal Amount { get; set; }
        public DateTime ObservedAt { get; set; }
        public StreamingDetail Detail { get; set; } = new();
    }

    private sealed class StreamingDetail
    {
        public string Value { get; set; } = "";
    }

    private sealed class CustomKeyRecord : Entity<CustomKeyRecord, int>
    {
    }

    private sealed class CustomKeyRepository :
        IDataRepository<CustomKeyRecord, int>,
        IQueryRepository<CustomKeyRecord, int>,
        IDescribesCapabilities
    {
        public int QueryCalls { get; private set; }

        public void Describe(ICapabilities capabilities)
            => capabilities.Add(DataCaps.Query.ProviderBoundedPaging);

        public Task<RepositoryQueryResult<CustomKeyRecord>> Query(
            QueryDefinition query,
            CancellationToken ct = default)
        {
            QueryCalls++;
            throw new InvalidOperationException("A custom-key rejection must occur before provider I/O.");
        }

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
            => throw new InvalidOperationException("A custom-key rejection must not count.");

        public Task<CustomKeyRecord?> Get(int id, CancellationToken ct = default)
            => Task.FromResult<CustomKeyRecord?>(null);

        public Task<IReadOnlyList<CustomKeyRecord?>> GetMany(
            IEnumerable<int> ids,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CustomKeyRecord?>>([]);

        public Task<CustomKeyRecord> Upsert(CustomKeyRecord model, CancellationToken ct = default)
            => Task.FromResult(model);

        public Task<bool> Delete(int id, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> UpsertMany(IEnumerable<CustomKeyRecord> models, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DeleteMany(IEnumerable<int> ids, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DeleteAll(CancellationToken ct = default) => Task.FromResult(0);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => Task.FromResult(0L);
        public IBatchSet<CustomKeyRecord, int> CreateBatch() => throw new NotSupportedException();
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed record StreamingAxis(string Value);

    private sealed class StreamingAxisCarrier : IKoanContextCarrier
    {
        public string AxisKey => "stream-spec";
        public ContextIngressTrust MinimumIngressTrust => ContextIngressTrust.HostTrusted;
        public string? Capture() => KoanContext.Get<StreamingAxis>()?.Value;
        public IDisposable Restore(string captured) => KoanContext.Push(new StreamingAxis(captured));
        public IDisposable Suppress() => KoanContext.Suppress<StreamingAxis>();
    }

    private sealed class FakeStreamingAdapterFactory(FakeStreamingRepository repository) : IDataAdapterFactory
    {
        public const string ProviderId = "streaming-spec";

        public string Provider => ProviderId;

        public bool CanHandle(string provider)
            => string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase);

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider services, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
        {
            if (typeof(TEntity) != typeof(StreamingRecord) || typeof(TKey) != typeof(string))
                throw new InvalidOperationException($"The streaming spec adapter cannot create {typeof(TEntity).Name}<{typeof(TKey).Name}>.");

            return (IDataRepository<TEntity, TKey>)(object)repository;
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

    private sealed class FakeStreamingRepository :
        IDataRepository<StreamingRecord, string>,
        IQueryRepository<StreamingRecord, string>,
        IDescribesCapabilities
    {
        private IReadOnlyList<StreamingRecord> _items = [];
        private bool _advertiseBoundedPaging = true;
        private FilterSupport _filterSupport = FilterSupport.Full;
        private bool _paginationHandled = true;
        private bool _sortHandled = true;
        private bool _returnOneExtraCandidate;

        public List<QueryDefinition> QueryCalls { get; } = [];
        public List<string?> ObservedPartitions { get; } = [];
        public List<string?> ObservedSources { get; } = [];
        public List<string?> ObservedAxes { get; } = [];
        public int CountCalls { get; private set; }

        public void Reset(
            IReadOnlyList<StreamingRecord> items,
            bool advertiseBoundedPaging = true,
            FilterSupport? filterSupport = null,
            bool paginationHandled = true,
            bool sortHandled = true,
            bool returnOneExtraCandidate = false)
        {
            _items = items;
            _advertiseBoundedPaging = advertiseBoundedPaging;
            _filterSupport = filterSupport ?? FilterSupport.Full;
            _paginationHandled = paginationHandled;
            _sortHandled = sortHandled;
            _returnOneExtraCandidate = returnOneExtraCandidate;
            QueryCalls.Clear();
            ObservedPartitions.Clear();
            ObservedSources.Clear();
            ObservedAxes.Clear();
            CountCalls = 0;
        }

        public void Describe(ICapabilities capabilities)
        {
            capabilities
                .Add(DataCaps.Query.Linq)
                .Add(DataCaps.Query.Filter, _filterSupport);

            if (_advertiseBoundedPaging)
                capabilities.Add(DataCaps.Query.ProviderBoundedPaging);
        }

        public Task<RepositoryQueryResult<StreamingRecord>> Query(QueryDefinition query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            QueryCalls.Add(query);
            ObservedPartitions.Add(EntityContext.Current?.Partition);
            ObservedSources.Add(EntityContext.Current?.Source);
            ObservedAxes.Add(KoanContext.Get<StreamingAxis>()?.Value);

            IEnumerable<StreamingRecord> candidates = _items;
            if (query.Filter is not null)
                candidates = candidates.Where(InMemoryFilterEvaluator.Compile<StreamingRecord>(query.Filter));

            // Sequence and Id deliberately have the same order. The fake reports every requested sort
            // as handled, while retaining a transparent deterministic implementation for the contract proof.
            candidates = candidates.OrderBy(record => record.Id, StringComparer.Ordinal);

            var pageSize = query.EffectivePageSize();
            var skip = Math.Max(query.EffectivePage() - 1, 0) * pageSize;
            var take = pageSize + (_returnOneExtraCandidate ? 1 : 0);
            var page = candidates.Skip(skip).Take(take).ToList();

            return Task.FromResult(new RepositoryQueryResult<StreamingRecord>
            {
                Items = page,
                TotalCount = null,
                IsEstimate = false,
                PaginationHandled = _paginationHandled,
                SortHandled = _sortHandled
                    ? new HashSet<SortSpec>(query.Sort)
                    : RepositoryQueryResult<StreamingRecord>.NoSortHandled
            });
        }

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CountCalls++;
            return Task.FromResult(new CountResult(_items.Count, false));
        }

        public Task EnsureReady(CancellationToken ct = default) => Task.CompletedTask;

        public Task<StreamingRecord?> Get(string id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(record => record.Id == id));

        public Task<IReadOnlyList<StreamingRecord?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var byId = _items.ToDictionary(record => record.Id, StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<StreamingRecord?>>(
                ids.Select(id => byId.GetValueOrDefault(id)).ToList());
        }

        public Task<StreamingRecord> Upsert(StreamingRecord model, CancellationToken ct = default)
            => Task.FromResult(model);

        public Task<int> UpsertMany(IEnumerable<StreamingRecord> models, CancellationToken ct = default)
            => Task.FromResult(models.Count());

        public Task<bool> Delete(string id, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DeleteAll(CancellationToken ct = default) => Task.FromResult(0);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => Task.FromResult(0L);
        public IBatchSet<StreamingRecord, string> CreateBatch() => throw new NotSupportedException();
    }
}
