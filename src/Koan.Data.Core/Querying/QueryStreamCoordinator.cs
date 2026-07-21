using System.Runtime.CompilerServices;
using Koan.Core.Diagnostics;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Core.Querying;

/// <summary>
/// Composes one provider-bounded query page at a time into the public async sequence. This is the
/// only Data.Core owner of streaming pagination, ordering, residual evaluation, and rejection facts.
/// </summary>
internal static class QueryStreamCoordinator
{
    public static async IAsyncEnumerable<TEntity> Execute<TEntity, TKey>(
        IDataRepository<TEntity, TKey> repository,
        QueryDefinition query,
        string provider,
        int? requestedBatchSize,
        IKoanRuntimeFactRecorder? facts,
        Func<IDisposable> enterContext,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(enterContext);

        var batchSize = requestedBatchSize ?? Infrastructure.Constants.Defaults.UnboundedLoopPageSize;
        if (batchSize <= 0)
            throw Reject<TEntity>(facts, provider,
                Infrastructure.Constants.Diagnostics.Reasons.InvalidStreamBatchSize,
                "Use a positive batch size, or omit it to use Koan's bounded default.", batchSize);

        ct.ThrowIfCancellationRequested();

        var capabilities = DataCaps.Describe(repository, provider);
        if (!capabilities.Has(DataCaps.Query.ProviderBoundedPaging))
            throw Reject<TEntity>(facts, provider,
                Infrastructure.Constants.Diagnostics.Reasons.MissingProviderBoundedPaging,
                "Route this Entity to an adapter that advertises provider-bounded paging, or materialize the query explicitly.",
                batchSize);

        if (repository is not IQueryRepository<TEntity, TKey> queryRepository)
            throw Reject<TEntity>(facts, provider,
                Infrastructure.Constants.Diagnostics.Reasons.MissingProviderBoundedPaging,
                "Route this Entity to a query-capable adapter that implements provider-bounded paging.", batchSize);

        query = query.WithoutPagination().WithCountStrategy(null);

        // The currently qualified adapter contract has one deliberately conservative portable sort
        // floor. Validate the caller's semantic order before appending Koan's provider-stable Entity Id
        // tie-breaker: a physical key may be stable enough to prevent page drift without promising CLR
        // numeric or cross-provider string ordering when the caller explicitly sorts by Id.
        if (query.Sort.Any(spec =>
                spec.Path.TraversesCollection ||
                spec.Path.Members.Count != 1 ||
                IsEntityIdentifier<TEntity, TKey>(spec.Path) ||
                !TypeClassification.IsPortableStreamSortScalar(spec.Path.ValueType)))
            throw Reject<TEntity>(facts, provider,
                Infrastructure.Constants.Diagnostics.Reasons.UnsupportedStreamSort,
                "Use a proven portable top-level order, or materialize the query before applying this sort.",
                batchSize);

        query = EnsureTotalOrder<TEntity, TKey>(query);
        if (query.Sort.Any(spec =>
                IsEntityIdentifier<TEntity, TKey>(spec.Path) &&
                !IsProviderStableIdentifier<TEntity, TKey>(spec.Path)))
            throw Reject<TEntity>(facts, provider,
                Infrastructure.Constants.Diagnostics.Reasons.UnsupportedStreamSort,
                "Use an Entity identifier shape with a proven provider-stable stream tie-breaker, or materialize the query explicitly.",
                batchSize);

        var filterSupport = capabilities.Detail<FilterSupport>(DataCaps.Query.Filter) ?? FilterSupport.None;
        var (adapterBase, residual) = FilterPushdownCoordinator.Plan(query, filterSupport, typeof(TEntity));
        var residualPredicate = residual is null ? null : InMemoryFilterEvaluator.Compile<TEntity>(residual);

        var pageNumber = 1;
        var selectedRecorded = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var adapterPage = adapterBase
                .WithPagination(pageNumber, batchSize)
                .WithCountStrategy(null);
            RepositoryQueryResult<TEntity> result;
            using (enterContext())
                result = await queryRepository.Query(adapterPage, ct).ConfigureAwait(false);

            // Validate the complete candidate page before yielding from it. A provider cannot emit a
            // trustworthy prefix and then reveal that it ignored the requested bound or total order.
            if (!result.PaginationHandled)
                throw Reject<TEntity>(facts, provider,
                    Infrastructure.Constants.Diagnostics.Reasons.PaginationNotHandled,
                    "Use an adapter that applies the requested page in the provider before materialization.", batchSize);
            if (result.Items.Count > batchSize)
                throw Reject<TEntity>(facts, provider,
                    Infrastructure.Constants.Diagnostics.Reasons.StreamPageLimitExceeded,
                    $"The provider returned more than the requested {batchSize} candidates; correct or replace the adapter.",
                    batchSize);
            if (!result.SortFullyHandled(adapterPage))
                throw Reject<TEntity>(facts, provider,
                    Infrastructure.Constants.Diagnostics.Reasons.StreamSortNotHandled,
                    "Use a provider-handled portable top-level order, or materialize the query explicitly.", batchSize);

            if (!selectedRecorded)
            {
                Record<TEntity>(facts, provider, KoanFactState.Selected,
                    $"Selected provider-bounded paging for {typeof(TEntity).Name} with a maximum candidate page of {batchSize}.",
                    Infrastructure.Constants.Diagnostics.Reasons.ProviderBoundedPaging, null);
                selectedRecorded = true;
            }

            var candidateCount = result.Items.Count;
            foreach (var item in result.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (residualPredicate is null || residualPredicate(item))
                    yield return item;
            }

            if (candidateCount < batchSize) yield break;

            // Qualified adapters currently express OFFSET/Skip as Int32. The next provider request
            // would calculate pageNumber * batchSize, so refuse before that multiplication can wrap.
            if (pageNumber == int.MaxValue || (long)pageNumber * batchSize > int.MaxValue)
                throw Reject<TEntity>(facts, provider,
                    Infrastructure.Constants.Diagnostics.Reasons.StreamPageLimitExceeded,
                    "Narrow the query; its numbered-page range exceeded the supported limit.", batchSize);
            pageNumber++;
        }
    }

    private static QueryDefinition EnsureTotalOrder<TEntity, TKey>(QueryDefinition query)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (!query.HasSort)
            return query.WithSort<TEntity>(sort => sort.OrderBy(entity => entity.Id));

        var interfaceId = ExpressionMemberPath.From<TEntity, TKey>(entity => entity.Id).Members[0];
        var concreteId = AggregateMetadata.GetIdSpec(typeof(TEntity))?.Prop;
        var hasId = query.Sort.Any(spec =>
            !spec.Path.TraversesCollection &&
            spec.Path.Members.Count == 1 &&
            (spec.Path.Members[0].Equals(interfaceId) || spec.Path.Members[0].Equals(concreteId)));
        return hasId ? query : query.ThenBy<TEntity, TKey>(entity => entity.Id);
    }

    private static bool IsProviderStableIdentifier<TEntity, TKey>(MemberPath path)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (!IsEntityIdentifier<TEntity, TKey>(path)) return false;

        return IsProviderStableIdentifierType(path.ValueType);
    }

    // This floor is intentionally separate from caller-sort admission. The shared six-adapter
    // corpus proves Koan's normal string key as an opaque page tie-breaker; no custom key shape is
    // inferred merely because business fields of that CLR type have portable ordering.
    internal static bool IsProviderStableIdentifierType(Type type)
        => type == typeof(string);

    private static bool IsEntityIdentifier<TEntity, TKey>(MemberPath path)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        if (path.TraversesCollection || path.Members.Count != 1) return false;

        var member = path.Members[0];
        var interfaceId = ExpressionMemberPath.From<TEntity, TKey>(entity => entity.Id).Members[0];
        var concreteId = AggregateMetadata.GetIdSpec(typeof(TEntity))?.Prop;
        return member.Equals(interfaceId) || member.Equals(concreteId);
    }

    private static QueryStreamRejectedException Reject<TEntity>(
        IKoanRuntimeFactRecorder? facts,
        string provider,
        string reason,
        string correction,
        int? batchSize)
    {
        Record<TEntity>(facts, provider, KoanFactState.Rejected,
            $"Rejected unbounded stream execution for {typeof(TEntity).Name}.", reason, correction);
        return new QueryStreamRejectedException(
            typeof(TEntity).FullName ?? typeof(TEntity).Name,
            provider,
            reason,
            correction,
            batchSize);
    }

    private static void Record<TEntity>(
        IKoanRuntimeFactRecorder? facts,
        string provider,
        KoanFactState state,
        string summary,
        string reason,
        string? correction)
    {
        if (facts is null) return;
        var entity = typeof(TEntity).FullName ?? typeof(TEntity).Name;
        var subject = $"stream:{entity}";
        facts.Record(new KoanFactDescriptor(
            Infrastructure.Constants.Diagnostics.Codes.StreamExecution,
            KoanFactKind.Capability,
            state,
            subject,
            summary,
            reason,
            correction,
            "Koan.Data.Core.Querying",
            $"{provider}:{entity}"));
    }
}
