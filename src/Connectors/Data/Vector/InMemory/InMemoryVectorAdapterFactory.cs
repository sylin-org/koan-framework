using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.InMemory;

/// <summary>Host-owned factory for the infrastructure-free exact Vector adapter.</summary>
[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
public sealed class InMemoryVectorAdapterFactory : IVectorAdapterFactory, IDisposable
{
    private readonly InMemoryVectorOptions _options;
    private readonly InMemoryVectorStoreCatalog _stores;

    public InMemoryVectorAdapterFactory(IOptions<InMemoryVectorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = Validate(options.Value);
        _stores = new InMemoryVectorStoreCatalog(_options.MaxSpaces);
    }

    public string Provider => Infrastructure.Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => Infrastructure.Constants.Provider.Aliases;
    public bool IsAutomaticFloor => true;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.AsIs,
        PartitionSeparator = '#',
        Partition = PartitionTokenPolicy.Default
    };

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        VectorSpacePlan plan)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Dimensions > _options.MaxDimensions)
            throw new InvalidOperationException(
                $"Vector space '{plan.Name}' declares {plan.Dimensions} dimensions; InMemory is bounded to {_options.MaxDimensions}. " +
                "Reduce Dimensions or increase Koan:Data:Vector:InMemory:MaxDimensions.");
        if (plan.Visibility != VectorVisibility.Session)
            throw new NotSupportedException(
                "InMemory Vector is immediately session-visible and does not simulate Eventual visibility. Use Visibility(Session).");
        return new InMemoryVectorRepository<TEntity, TKey>(services, this, plan, _stores, _options);
    }

    public void Dispose() => _stores.Dispose();

    private static InMemoryVectorOptions Validate(InMemoryVectorOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.MaxSpaces <= 0) throw Invalid(nameof(value.MaxSpaces));
        if (value.MaxPointsPerSpace <= 0) throw Invalid(nameof(value.MaxPointsPerSpace));
        if (value.MaxDimensions <= 0) throw Invalid(nameof(value.MaxDimensions));
        if (value.MaxMetadataBytesPerPoint <= 0) throw Invalid(nameof(value.MaxMetadataBytesPerPoint));
        return value;
    }

    private static InvalidOperationException Invalid(string name) =>
        new($"InMemoryVectorOptions.{name} must be greater than zero.");
}
