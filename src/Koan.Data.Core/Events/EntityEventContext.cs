using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Core.Events;

/// <summary>
/// Runtime context supplied to entity lifecycle hooks.
/// </summary>
public sealed class EntityEventContext<TEntity>
    where TEntity : class
{
    private static readonly Lazy<IReadOnlyDictionary<string, Func<TEntity, object?>>> PropertyGetters
        = new(CreatePropertyGetterMap);

    private readonly HashSet<string> _protectedMembers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _protectedSnapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _items = new(StringComparer.Ordinal);

    internal EntityEventContext(
        TEntity current,
        EntityEventOperation operation,
        EntityEventPrior<TEntity> prior,
        EntityEventOperationState state,
        CancellationToken cancellationToken)
    {
        Current = current ?? throw new ArgumentNullException(nameof(current));
        Operation = state;
        LifecycleOperation = operation;
        Prior = prior;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the current entity value flowing through the pipeline.
    /// </summary>
    public TEntity Current { get; internal set; }

    /// <summary>
    /// Gets the scoped operation state shared between handlers.
    /// </summary>
    public EntityEventOperationState Operation { get; }

    /// <summary>
    /// Gets the lazily-loaded prior entity snapshot.
    /// </summary>
    public EntityEventPrior<TEntity> Prior { get; }

    /// <summary>
    /// Gets the cancellation token for the current operation.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the lifecycle operation currently executing.
    /// </summary>
    public EntityEventOperation LifecycleOperation { get; }

    /// <summary>
    /// Arbitrary state bag shared between handlers.
    /// </summary>
    public IDictionary<string, object?> Items => _items;

    /// <summary>
    /// Marks a property as immutable for the duration of the lifecycle execution.
    /// </summary>
    public void Protect(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name must be provided.", nameof(propertyName));
        }

        var getters = PropertyGetters.Value;
        if (!getters.ContainsKey(propertyName))
        {
            throw new InvalidOperationException($"Property '{propertyName}' does not exist on {typeof(TEntity).Name} or is not readable.");
        }

        _protectedMembers.Add(propertyName);
    }

    /// <summary>
    /// Protects every readable property on the entity.
    /// </summary>
    public void ProtectAll()
    {
        foreach (var property in PropertyGetters.Value.Keys)
        {
            _protectedMembers.Add(property);
        }
    }

    /// <summary>
    /// Allows the specified property to be mutated even if protected via <see cref="ProtectAll"/>.
    /// </summary>
    public void AllowMutation(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name must be provided.", nameof(propertyName));
        }

        _protectedMembers.Remove(propertyName);
        _protectedSnapshots.Remove(propertyName);
    }

    /// <summary>
    /// Creates a <see cref="EntityEventResult"/> signalling continued execution.
    /// </summary>
    public EntityEventResult Proceed() => EntityEventResult.Proceed();

    /// <summary>
    /// Creates a <see cref="EntityEventResult"/> signalling cancellation.
    /// </summary>
    public EntityEventResult Cancel(string reason, string? code = null) => EntityEventResult.Cancel(reason, code);

    internal void CaptureProtectionSnapshot()
    {
        if (_protectedMembers.Count == 0)
        {
            return;
        }

        var getters = PropertyGetters.Value;
        foreach (var member in _protectedMembers)
        {
            if (getters.TryGetValue(member, out var getter))
            {
                _protectedSnapshots[member] = getter(Current);
            }
        }
    }

    internal void ValidateProtection()
    {
        if (_protectedSnapshots.Count == 0)
        {
            return;
        }

        var getters = PropertyGetters.Value;
        foreach (var entry in _protectedSnapshots)
        {
            if (getters.TryGetValue(entry.Key, out var getter))
            {
                var currentValue = getter(Current);
                if (!Equals(currentValue, entry.Value))
                {
                    throw new InvalidOperationException($"Entity field '{entry.Key}' is protected and cannot be mutated during lifecycle execution.");
                }
            }
        }
    }

    internal void UpdateCurrent(TEntity current)
    {
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    private static IReadOnlyDictionary<string, Func<TEntity, object?>> CreatePropertyGetterMap()
    {
        var properties = typeof(TEntity)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead);

        var dictionary = new Dictionary<string, Func<TEntity, object?>>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var getter = BuildGetter(property);
            dictionary[property.Name] = getter;
        }

        return dictionary;
    }

    private static Func<TEntity, object?> BuildGetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(TEntity), "instance");
        var propertyAccess = Expression.Property(instance, property);
        var convert = Expression.Convert(propertyAccess, typeof(object));
        var lambda = Expression.Lambda<Func<TEntity, object?>>(convert, instance);
        return lambda.Compile();
    }
}
