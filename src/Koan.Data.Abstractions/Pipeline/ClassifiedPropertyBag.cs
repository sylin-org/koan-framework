using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// Caches classification metadata for one entity type: scans once for <c>[Classified]</c> properties (incl. the
/// <c>[Pii]</c> / <c>[Phi]</c> / <c>[Pci]</c> / <c>[Secret]</c> sugar) and Expression-compiles a getter/setter per
/// property — the classification analog of <c>TimestampPropertyBag</c>, which it deliberately mirrors, plus the
/// round-trip the timestamp bag lacks. Built once per entity type and memoized by
/// <see cref="ClassifiedFieldRegistry.ForType"/>. ARCH-0098 §1.
/// </summary>
public sealed class ClassifiedPropertyBag
{
    /// <summary>Whether this entity type declares any classified property — the per-type fast short-circuit.</summary>
    public bool HasClassifiedFields { get; }

    /// <summary>The classified-field descriptors for this type, in declaration order. Empty when none.</summary>
    public IReadOnlyList<ClassifiedFieldDescriptor> Descriptors { get; }

    /// <summary>
    /// Scans <paramref name="entityType"/> for classified properties and compiles their accessors. A round-trip
    /// classified property must be both readable and writable (the read reverse must set plaintext back); a
    /// get-only / init-only property is skipped (it structurally cannot round-trip).
    /// </summary>
    public ClassifiedPropertyBag(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var descriptors = new List<ClassifiedFieldDescriptor>();
        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = property.GetCustomAttributes<ClassifiedAttribute>(inherit: true).FirstOrDefault();
            if (attribute is null) continue;
            if (!property.CanRead || !property.CanWrite) continue;   // round-trip requires both

            descriptors.Add(new ClassifiedFieldDescriptor(
                property,
                attribute.Category,
                attribute.Searchable,
                CompileGetter(entityType, property),
                CompileSetter(entityType, property)));
        }

        Descriptors = descriptors;
        HasClassifiedFields = descriptors.Count > 0;
    }

    /// <summary>Compiles a boxed getter: <c>(object e) =&gt; (object)((T)e).Prop</c>.</summary>
    private static Func<object, object?> CompileGetter(Type entityType, PropertyInfo property)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var body = Expression.Convert(
            Expression.Property(Expression.Convert(entityParam, entityType), property),
            typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, entityParam).Compile();
    }

    /// <summary>Compiles a boxed setter: <c>(object e, object v) =&gt; ((T)e).Prop = (TProp)v</c>.</summary>
    private static Action<object, object?> CompileSetter(Type entityType, PropertyInfo property)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var assign = Expression.Assign(
            Expression.Property(Expression.Convert(entityParam, entityType), property),
            Expression.Convert(valueParam, property.PropertyType));
        return Expression.Lambda<Action<object, object?>>(assign, entityParam, valueParam).Compile();
    }
}
