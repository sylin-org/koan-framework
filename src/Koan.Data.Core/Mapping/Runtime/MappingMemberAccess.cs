using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Mapping.Runtime;

internal sealed class MappingMemberAccess
{
    private readonly AccessSegment[] _segments;

    private MappingMemberAccess(MappingPath path, Type valueType, AccessSegment[] segments)
    {
        Path = path;
        ValueType = valueType;
        _segments = segments;
    }

    public MappingPath Path { get; }
    public Type ValueType { get; }
    public bool CanWrite => _segments.Length > 0 && _segments[^1].Setter is not null;

    public static MappingMemberAccess Compile(Type rootType, MappingPath path, bool requireWrite)
    {
        if (path.IsRoot) return new MappingMemberAccess(path, rootType, []);
        var current = rootType;
        var segments = new List<AccessSegment>();
        for (var index = 0; index < path.Segments.Count; index++)
        {
            var name = path.Segments[index];
            var property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Logical path '{path}' does not resolve on '{rootType.FullName}'.");
            if (property.GetIndexParameters().Length != 0 || property.GetMethod is null)
                throw new InvalidOperationException($"Logical path '{path}' contains an unreadable property '{name}'.");
            var setter = property.SetMethod is null ? null : CompileSetter(property);
            Func<object>? factory = null;
            var intermediate = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (requireWrite && index < path.Segments.Count - 1)
            {
                if (intermediate.IsValueType)
                    throw new InvalidOperationException(
                        $"Logical path '{path}' traverses value-type property '{name}'. Map the containing value as Object or provide a codec.");
                if (setter is null)
                    throw new InvalidOperationException($"Logical path '{path}' cannot construct read-only intermediate property '{name}'.");
                factory = CompileFactory(intermediate, path);
            }
            segments.Add(new AccessSegment(CompileGetter(property), setter, factory, property.PropertyType));
            // Read-only derived paths are observations over an authoritative object value. They only need
            // getters, and may safely traverse nullable structs and immutable records because they never
            // construct or assign an intermediate instance. Writable canonical paths retain the stricter
            // construction contract above.
            current = intermediate;
        }

        var result = new MappingMemberAccess(path, current, segments.ToArray());
        if (requireWrite && !result.CanWrite)
            throw new InvalidOperationException($"Logical path '{path}' must be writable for hydration.");
        return result;
    }

    public object? Get(object root)
    {
        ArgumentNullException.ThrowIfNull(root);
        object? current = root;
        foreach (var segment in _segments)
        {
            if (current is null) return null;
            current = segment.Getter(current);
        }
        return current;
    }

    public void Set(object root, object? value)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_segments.Length == 0)
            throw new InvalidOperationException("The aggregate root is replaced by the structured materializer, not a member setter.");
        object current = root;
        for (var index = 0; index < _segments.Length - 1; index++)
        {
            var segment = _segments[index];
            var next = segment.Getter(current);
            if (next is null)
            {
                next = segment.Factory!();
                segment.Setter!(current, next);
            }
            current = next;
        }
        _segments[^1].Setter!(current, value);
    }

    private static Func<object, object?> CompileGetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var access = Expression.Property(Expression.Convert(instance, property.DeclaringType!), property);
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), instance).Compile();
    }

    private static Action<object, object?> CompileSetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");
        var call = Expression.Call(
            Expression.Convert(instance, property.DeclaringType!),
            property.SetMethod!,
            Expression.Convert(value, property.PropertyType));
        return Expression.Lambda<Action<object, object?>>(call, instance, value).Compile();
    }

    private static Func<object> CompileFactory(Type type, MappingPath path)
    {
        var ctor = type.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"Logical path '{path}' requires a public parameterless constructor on '{type.FullName}'.");
        return Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object))).Compile();
    }

    private sealed record AccessSegment(
        Func<object, object?> Getter,
        Action<object, object?>? Setter,
        Func<object>? Factory,
        Type Type);
}
