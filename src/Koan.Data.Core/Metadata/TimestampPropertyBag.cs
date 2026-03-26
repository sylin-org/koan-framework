using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Core.Metadata;

/// <summary>
/// Caches metadata for [Timestamp] attribute auto-update functionality.
/// Performs reflection scan once per entity type and compiles fast delegates.
/// Cached at RepositoryFacade construction for hot path optimization.
///
/// Two behaviors:
///   - OnSave = false (default): set-once semantics — stamped only when value is default (CreatedAt).
///   - OnSave = true: set-always semantics — updated on every save (UpdatedAt / LastModified).
/// </summary>
public sealed class TimestampPropertyBag
{
    /// <summary>
    /// Indicates whether this entity type has any [Timestamp] properties.
    /// Used for fast short-circuit evaluation on hot path.
    /// </summary>
    public bool HasTimestamp { get; }

    private readonly List<Action<object, DateTimeOffset>> _setAlwaysProperties = [];
    private readonly List<(Func<object, DateTimeOffset> Getter, Action<object, DateTimeOffset> Setter)> _setOnceProperties = [];

    /// <summary>
    /// Scans entity type for [Timestamp] DateTimeOffset properties and compiles delegates.
    /// Executes once per entity type at RepositoryFacade construction.
    /// </summary>
    public TimestampPropertyBag(Type entityType)
    {
        if (entityType == null) throw new ArgumentNullException(nameof(entityType));

        var timestampProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.GetCustomAttribute<TimestampAttribute>() != null &&
                p.PropertyType == typeof(DateTimeOffset) &&
                p.CanWrite)
            .ToList();

        HasTimestamp = timestampProperties.Count > 0;

        foreach (var property in timestampProperties)
        {
            var attribute = property.GetCustomAttribute<TimestampAttribute>()!;

            if (attribute.OnSave)
            {
                _setAlwaysProperties.Add(CompileSetter(entityType, property));
            }
            else
            {
                _setOnceProperties.Add((
                    CompileGetter(entityType, property),
                    CompileSetter(entityType, property)));
            }
        }
    }

    /// <summary>
    /// Updates [Timestamp] properties according to their OnSave semantics.
    /// Hot path method - optimized for branch prediction.
    /// </summary>
    public void UpdateTimestamp(object entity)
    {
        if (!HasTimestamp) return;

        var now = DateTimeOffset.UtcNow;

        foreach (var setter in _setAlwaysProperties)
            setter(entity, now);

        foreach (var (getter, setter) in _setOnceProperties)
        {
            var current = getter(entity);
            if (current == default)
                setter(entity, now);
        }
    }

    /// <summary>
    /// Compiles a fast setter delegate using Expression trees.
    /// Avoids reflection overhead on every save operation.
    /// </summary>
    private static Action<object, DateTimeOffset> CompileSetter(Type entityType, PropertyInfo property)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var valueParam = Expression.Parameter(typeof(DateTimeOffset), "value");

        var castEntity = Expression.Convert(entityParam, entityType);
        var propertyAccess = Expression.Property(castEntity, property);
        var assignExpression = Expression.Assign(propertyAccess, valueParam);

        var lambda = Expression.Lambda<Action<object, DateTimeOffset>>(
            assignExpression,
            entityParam,
            valueParam);

        return lambda.Compile();
    }

    /// <summary>
    /// Compiles a fast getter delegate using Expression trees.
    /// Used for set-once properties to check if value is default before stamping.
    /// </summary>
    private static Func<object, DateTimeOffset> CompileGetter(Type entityType, PropertyInfo property)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");

        var castEntity = Expression.Convert(entityParam, entityType);
        var propertyAccess = Expression.Property(castEntity, property);

        var lambda = Expression.Lambda<Func<object, DateTimeOffset>>(
            propertyAccess,
            entityParam);

        return lambda.Compile();
    }
}
