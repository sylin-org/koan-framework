using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Koan.Data.Core.Metadata;

/// <summary>
/// Caches metadata for [Timestamp] attribute auto-update functionality.
/// Performs reflection scan once per entity type and compiles fast setter delegate.
/// Cached at RepositoryFacade construction for hot path optimization.
/// </summary>
internal sealed class TimestampPropertyBag
{
    /// <summary>
    /// Indicates whether this entity type has a [Timestamp] property.
    /// Used for fast short-circuit evaluation on hot path.
    /// </summary>
    public bool HasTimestamp { get; }

    private readonly Action<object, DateTimeOffset>? _compiledSetter;

    /// <summary>
    /// Scans entity type for [Timestamp] DateTimeOffset property and compiles setter.
    /// Executes once per entity type at RepositoryFacade construction.
    /// </summary>
    public TimestampPropertyBag(Type entityType)
    {
        if (entityType == null) throw new ArgumentNullException(nameof(entityType));

        // Scan for [Timestamp] attribute on DateTimeOffset properties
        var timestampProperty = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p =>
                p.GetCustomAttribute<TimestampAttribute>() != null &&
                p.PropertyType == typeof(DateTimeOffset) &&
                p.CanWrite);

        HasTimestamp = timestampProperty != null;

        if (HasTimestamp)
        {
            _compiledSetter = CompileSetter(entityType, timestampProperty!);
        }
    }

    /// <summary>
    /// Updates the [Timestamp] property to DateTimeOffset.UtcNow if present.
    /// Hot path method - optimized for branch prediction.
    /// </summary>
    public void UpdateTimestamp(object entity)
    {
        if (HasTimestamp && _compiledSetter != null)
        {
            _compiledSetter(entity, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Compiles a fast setter delegate using Expression trees.
    /// Avoids reflection overhead on every save operation.
    /// </summary>
    private static Action<object, DateTimeOffset> CompileSetter(Type entityType, PropertyInfo property)
    {
        // Parameters: (object entity, DateTimeOffset value)
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var valueParam = Expression.Parameter(typeof(DateTimeOffset), "value");

        // Cast object to actual entity type
        var castEntity = Expression.Convert(entityParam, entityType);

        // Access the property
        var propertyAccess = Expression.Property(castEntity, property);

        // Assign: entity.Property = value
        var assignExpression = Expression.Assign(propertyAccess, valueParam);

        // Compile to delegate
        var lambda = Expression.Lambda<Action<object, DateTimeOffset>>(
            assignExpression,
            entityParam,
            valueParam);

        return lambda.Compile();
    }
}
