using System;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Runtime context shared between pipeline phases and observers.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
public sealed class CanonPipelineContext<TModel> : ICanonPipelineContext
    where TModel : CanonEntity<TModel>, new()
{
    private readonly Dictionary<string, object?> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="CanonPipelineContext{TModel}"/>.
    /// </summary>
    public CanonPipelineContext(TModel entity, CanonMetadata metadata, CanonizationOptions options, ICanonPersistence persistence, IServiceProvider? services = null)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        Services = services ?? EmptyServiceProvider.Instance;
    }

    /// <summary>
    /// Canonical entity instance.
    /// </summary>
    public TModel Entity { get; }

    /// <summary>
    /// Current metadata snapshot.
    /// </summary>
    public CanonMetadata Metadata { get; private set; }

    /// <summary>
    /// Canonization options in effect.
    /// </summary>
    public CanonizationOptions Options { get; private set; }

    /// <summary>
    /// Service provider for resolving dependencies within contributors.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Persistence abstraction for working with canonical storage.
    /// </summary>
    public ICanonPersistence Persistence { get; }

    /// <summary>
    /// Associated stage record, if the payload originated from staging.
    /// </summary>
    public CanonStage<TModel>? Stage { get; private set; }

    /// <inheritdoc />
    public Type EntityType => typeof(TModel);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Items => _items;

    /// <summary>
    /// Replaces the metadata snapshot.
    /// </summary>
    public void ApplyMetadata(CanonMetadata metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>
    /// Replaces the options snapshot.
    /// </summary>
    public void ApplyOptions(CanonizationOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Associates a stage record with the context.
    /// </summary>
    public void AttachStage(CanonStage<TModel> stage)
    {
        Stage = stage ?? throw new ArgumentNullException(nameof(stage));
    }

    /// <summary>
    /// Adds or replaces a context item.
    /// </summary>
    public void SetItem(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key must be provided.", nameof(key));
        }

        _items[key] = value;
    }

    /// <inheritdoc />
    public bool TryGetItem<TValue>(string key, out TValue? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = default;
            return false;
        }

        if (_items.TryGetValue(key, out var stored) && stored is TValue cast)
        {
            value = cast;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Minimal service provider used when no services are available.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        private EmptyServiceProvider() { }

        public object? GetService(Type serviceType)
            => throw new InvalidOperationException($"No service provider is available in this context. Cannot resolve service of type '{serviceType?.FullName ?? "null"}'.");
    }
}
