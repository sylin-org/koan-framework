using System.Reflection;
using Koan.Core.Hosting.Registry;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Pipeline;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Querying;
using Koan.Data.Core.Routing;
using Koan.Data.Core.Semantics;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Core.Composition;

/// <summary>One immutable, exhaustive host-owned description of the application's concrete Entity roots.</summary>
internal sealed class DataApplicationManifest
{
    public DataApplicationManifest(
        IServiceProvider services,
        DataSegmentationPlan segmentation,
        IFieldTransformInspector transforms,
        IEnumerable<IReadFilterContributor> readContributors)
        : this(
            services,
            segmentation,
            transforms,
            readContributors,
            KoanRegistry.GetDiscoveredImplementors(typeof(IEntity)))
    {
    }

    internal DataApplicationManifest(
        IServiceProvider services,
        DataSegmentationPlan segmentation,
        IFieldTransformInspector transforms,
        IEnumerable<IReadFilterContributor> readContributors,
        IEnumerable<Type> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(segmentation);
        ArgumentNullException.ThrowIfNull(transforms);
        ArgumentNullException.ThrowIfNull(readContributors);
        ArgumentNullException.ThrowIfNull(entityTypes);

        var contributors = readContributors.ToArray();
        var concrete = entityTypes
            .Where(static type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
            .Distinct()
            .OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (var type in concrete) EntityTypeCatalog.Register(type);

        Roots = concrete
            .Select(EntityRootDescriptor.For)
            .GroupBy(static descriptor => descriptor.RootType)
            .Select(group => Compile(
                services,
                segmentation,
                transforms,
                contributors,
                group.Key,
                group.Select(static descriptor => descriptor.DeclaredType).ToArray()))
            .OrderBy(static root => root.RootIdentity, StringComparer.Ordinal)
            .ToArray();
    }

    internal IReadOnlyList<DataEntityRootPlan> Roots { get; }

    private static DataEntityRootPlan Compile(
        IServiceProvider services,
        DataSegmentationPlan segmentation,
        IFieldTransformInspector transforms,
        IReadOnlyList<IReadFilterContributor> contributors,
        Type root,
        IReadOnlyList<Type> familyTypes)
    {
        var descriptor = EntityRootDescriptor.For(root);
        var blockers = new List<DataManifestBlocker>();
        var scope = DataEntityRouteScope.Default;
        var family = familyTypes.Append(root).Distinct().ToArray();

        if (descriptor.KeyType != typeof(string))
            blockers.Add(new(
                "koan.data.cutover.key-unsupported",
                $"Entity root '{root.FullName}' uses key '{descriptor.KeyType.FullName}'.",
                "Use a provider-stable string Entity identity for this cutover envelope."));
        if (DatabaseRouteRegistry.AppliesTo(root))
        {
            scope = DataEntityRouteScope.OutsideDefault;
            blockers.Add(new(
                "koan.data.cutover.database-axis",
                $"Entity root '{root.FullName}' is routed by a Database axis.",
                "Exclude the root from default-route promotion or supply a finite axis inventory in a later envelope."));
        }
        if (root.GetCustomAttribute<SourceAdapterAttribute>(inherit: true) is not null ||
            root.GetCustomAttribute<DataAdapterAttribute>(inherit: true) is not null)
        {
            scope = DataEntityRouteScope.OutsideDefault;
        }
        var managedTypes = family.Where(type => ManagedFieldRegistry.ForType(type).Count != 0).ToArray();
        if (managedTypes.Length != 0)
            blockers.Add(new(
                "koan.data.cutover.managed-fields",
                $"Entity family '{root.FullName}' has framework-managed stored fields on {Types(managedTypes)}.",
                "Graduate a managed-field migration envelope before promoting this application."));
        var segmentedTypes = family.Where(type => !segmentation.For(type).IsEmpty).ToArray();
        if (segmentedTypes.Length != 0)
            blockers.Add(new(
                "koan.data.cutover.segmentation",
                $"Entity family '{root.FullName}' participates in shared-row segmentation on {Types(segmentedTypes)}.",
                "Graduate finite segmentation inventory before promoting this application."));
        var overrideTypes = family.Where(type => OperationOverrideRegistry.ForDelete(type) is not null).ToArray();
        if (overrideTypes.Length != 0)
            blockers.Add(new(
                "koan.data.cutover.operation-override",
                $"Entity family '{root.FullName}' has a Data operation override on {Types(overrideTypes)}.",
                "Remove the override or graduate its exact raw-state migration contract."));
        var transformedTypes = family.Where(transforms.HasTransformsFor).ToArray();
        if (transformedTypes.Length != 0)
            blockers.Add(new(
                "koan.data.cutover.stored-transform",
                $"Entity family '{root.FullName}' has stored-field transforms on {Types(transformedTypes)}.",
                "Graduate a transform-preserving raw envelope before promoting this application."));
        var filteredTypes = family.Where(type => contributors.Any(contributor =>
            contributor is not ManagedEqualityReadContributor && contributor.ExcludesFromCache(type))).ToArray();
        if (filteredTypes.Length != 0)
            blockers.Add(new(
                "koan.data.cutover.read-filter",
                $"Entity family '{root.FullName}' has a non-equality read filter on {Types(filteredTypes)}.",
                "Graduate the read-filter's complete physical-slice inventory before promotion."));

        IDataEntityRootAccessor? accessor = null;
        if (descriptor.KeyType == typeof(string))
        {
            var accessorType = typeof(DataEntityRootAccessor<>).MakeGenericType(root);
            accessor = (IDataEntityRootAccessor?)Activator.CreateInstance(accessorType, services)
                ?? throw new InvalidOperationException($"Could not compile the Data manifest accessor for '{root.FullName}'.");
        }

        return new DataEntityRootPlan(
            root,
            descriptor.KeyType,
            EntityTypeCatalog.TypeId(root),
            scope,
            familyTypes.OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal).ToArray(),
            blockers,
            accessor);
    }

    private static string Types(IEnumerable<Type> types)
        => string.Join(", ", types
            .Select(static type => $"'{type.FullName ?? type.Name}'")
            .Order(StringComparer.Ordinal));
}

internal enum DataEntityRouteScope
{
    Default,
    OutsideDefault
}

internal sealed record DataManifestBlocker(string Code, string Reason, string Correction);

internal sealed record DataEntityRootPlan(
    Type RootType,
    Type KeyType,
    string RootIdentity,
    DataEntityRouteScope RouteScope,
    IReadOnlyList<Type> FamilyTypes,
    IReadOnlyList<DataManifestBlocker> Blockers,
    IDataEntityRootAccessor? Accessor)
{
    internal bool IsEligible => RouteScope == DataEntityRouteScope.Default && Blockers.Count == 0 && Accessor is not null;
}

internal interface IDataEntityRootAccessor
{
    Type RootType { get; }
    string ExpectedContainer(IDataAdapterFactory factory);
    bool SupportsBoundedTraversal(IDataAdapterFactory factory, string source);
    IDataEntityRootSession Open(IDataAdapterFactory factory, string source);
}

internal interface IDataEntityRootSession
{
    Task EnsureReady(CancellationToken ct);
    Task<DataEntityPage> ReadPage(int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<object?>> ReadByIds(IReadOnlyList<string> ids, CancellationToken ct);
    Task Upsert(IReadOnlyList<object> entities, CancellationToken ct);
}

internal sealed record DataEntityPage(IReadOnlyList<object> Items, bool HasMore);

internal sealed class DataEntityRootAccessor<TEntity>(IServiceProvider services) : IDataEntityRootAccessor
    where TEntity : class, IEntity<string>
{
    public Type RootType => typeof(TEntity);

    public string ExpectedContainer(IDataAdapterFactory factory)
        => factory.ResolveStorage(typeof(TEntity), null, services);

    public bool SupportsBoundedTraversal(IDataAdapterFactory factory, string source)
    {
        var repository = factory.Create<TEntity, string>(services, source);
        return repository is IQueryRepository<TEntity, string> &&
               DataCaps.Describe(repository, factory.Provider).Has(DataCaps.Query.ProviderBoundedPaging);
    }

    public IDataEntityRootSession Open(IDataAdapterFactory factory, string source)
        => new Session(factory.Create<TEntity, string>(services, source), factory.Provider);

    private sealed class Session(
        IDataRepository<TEntity, string> repository,
        string provider) : IDataEntityRootSession
    {
        public Task EnsureReady(CancellationToken ct) => repository.EnsureReady(ct);

        public async Task<DataEntityPage> ReadPage(int page, int pageSize, CancellationToken ct)
        {
            if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
            var capabilities = DataCaps.Describe(repository, provider);
            if (!capabilities.Has(DataCaps.Query.ProviderBoundedPaging))
                throw new NotSupportedException(
                    $"Adapter '{provider}' does not advertise provider-bounded paging for '{typeof(TEntity).FullName}'.");
            if (repository is not IQueryRepository<TEntity, string> query)
                throw new NotSupportedException(
                    $"Adapter '{provider}' does not expose structured queries for '{typeof(TEntity).FullName}'.");

            var definition = QueryDefinition.All
                .WithSort<TEntity>(sort => sort.OrderBy(entity => entity.Id))
                .WithPagination(page, pageSize)
                .WithCountStrategy(null);
            var result = await query.Query(definition, ct).ConfigureAwait(false);
            QueryReceiptValidator.Validate(definition, result);
            if (!result.PaginationHandled || !result.SortFullyHandled(definition) || result.Items.Count > pageSize)
                throw new QueryReceiptRejectedException(
                    typeof(TEntity).FullName ?? typeof(TEntity).Name,
                    !result.PaginationHandled ? QueryReceiptAxis.Pagination : QueryReceiptAxis.Sort,
                    "Verified cutover requires provider-handled bounded pages in stable Entity-ID order.");
            return new DataEntityPage(result.Items.Cast<object>().ToArray(), result.Items.Count == pageSize);
        }

        public async Task<IReadOnlyList<object?>> ReadByIds(IReadOnlyList<string> ids, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return [];
            var requested = ids.ToHashSet(StringComparer.Ordinal);
            if (requested.Count != ids.Count)
                throw new InvalidDataException(
                    $"Verified cutover received duplicate identities for Entity root '{typeof(TEntity).FullName}'.");

            var returned = await repository.GetMany(ids, ct).ConfigureAwait(false);
            var byId = new Dictionary<string, TEntity>(StringComparer.Ordinal);
            foreach (var entity in returned)
            {
                if (entity is null) continue;
                if (!requested.Contains(entity.Id) || !byId.TryAdd(entity.Id, entity))
                    throw new InvalidDataException(
                        $"Adapter '{provider}' returned an unexpected or duplicate identity while verifying " +
                        $"Entity root '{typeof(TEntity).FullName}'.");
            }

            var aligned = new object?[ids.Count];
            for (var index = 0; index < ids.Count; index++)
                aligned[index] = byId.GetValueOrDefault(ids[index]);
            return aligned;
        }

        public async Task Upsert(IReadOnlyList<object> entities, CancellationToken ct)
        {
            var typed = new TEntity[entities.Count];
            for (var index = 0; index < entities.Count; index++)
                typed[index] = entities[index] as TEntity
                    ?? throw new InvalidDataException(
                        $"Cutover record {index} is not assignable to Entity root '{typeof(TEntity).FullName}'.");
            var affected = await repository.UpsertMany(typed, ct).ConfigureAwait(false);
            if (affected != typed.Length)
                throw new BulkMutationReceiptRejectedException(
                    typeof(TEntity).FullName ?? typeof(TEntity).Name,
                    typed.Length,
                    affected,
                    Koan.Data.Abstractions.Failures.DataCommitOutcome.Unknown);
        }
    }
}
