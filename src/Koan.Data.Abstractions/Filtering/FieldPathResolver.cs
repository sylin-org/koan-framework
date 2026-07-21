using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Resolves a <see cref="FieldPath"/> into a <see cref="ResolvedField"/> against an entity
/// type: exact-case member binding with an unambiguous case-insensitive fallback, nested dot-paths,
/// and leaf collection detection.
/// Strict — an unresolvable segment throws <see cref="InvalidFilterFieldException"/>.
///
/// Harvested from the sort path's <c>MemberPathResolver</c>; the two converge onto this in a
/// later phase so filter and sort share one field-binding contract.
/// </summary>
public static class FieldPathResolver
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public;

    // Resolution is a pure function of (rootType, path) and reflection-bound, so it is memoized: every
    // pushed filter AND sort spec resolves its field on the (hot) per-query translation path, and the
    // reflective member walk is otherwise repeated for the same handful of (type, path) pairs forever.
    // FieldPath itself is not value-equal (its Segments list compares by reference), so the key is the
    // joined path string — allocation-free for the common single-segment case. Only successful resolutions
    // are cached; an unresolvable path keeps throwing (the strict contract callers depend on).
    private static readonly ConcurrentDictionary<(Type Root, string Path, Type? Managed), ResolvedField> _cache = new();

    public static ResolvedField Resolve(Type rootType, FieldPath path)
    {
        if (rootType is null) throw new ArgumentNullException(nameof(rootType));
        if (path is null || path.Segments.Count == 0)
            throw new InvalidFilterFieldException(path?.ToString() ?? string.Empty, rootType!, string.Empty, "Field path is empty.");

        var key = (
            rootType,
            path.Segments.Count == 1 ? path.Segments[0] : string.Join('.', path.Segments),
            path.ManagedClrType);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var resolved = ResolveCore(rootType, path);
        _cache[key] = resolved;
        return resolved;
    }

    private static ResolvedField ResolveCore(Type rootType, FieldPath path)
    {
        if (path.ManagedClrType is { } declaredManagedType && path.Segments.Count == 1)
        {
            var storageName = path.Segments[0];
            var managedComparable = Nullable.GetUnderlyingType(declaredManagedType) ?? declaredManagedType;
            return new ResolvedField(
                rootType,
                Array.Empty<MemberInfo>(),
                declaredManagedType,
                managedComparable,
                TargetsCollection: false,
                ElementType: null,
                IsManaged: true,
                StorageName: storageName,
                CanonicalPath: FieldPath.Managed(storageName, declaredManagedType));
        }

        // Managed-field resolution (DATA-0105 §3b, Seam 3) — a single segment that matches a registered managed
        // field for this entity type resolves to a managed ResolvedField (no CLR member). Gated on registry
        // non-emptiness so the throw-path and success-only memoization below are byte-identical when no module
        // registers. Because the relational translator AND the pushability splitter both funnel through this one
        // resolver, this single change makes both managed-aware consistently.
        if (!ManagedFieldRegistry.IsEmpty && path.Segments.Count == 1)
        {
            var seg = path.Segments[0];
            var managed = ManagedFieldRegistry.ForType(rootType)
                .FirstOrDefault(d => string.Equals(d.StorageName, seg, StringComparison.Ordinal));
            if (managed is not null)
            {
                var managedComparable = Nullable.GetUnderlyingType(managed.ClrType) ?? managed.ClrType;
                return new ResolvedField(rootType, Array.Empty<MemberInfo>(), managed.ClrType, managedComparable,
                    TargetsCollection: false, ElementType: null, IsManaged: true, StorageName: managed.StorageName,
                    CanonicalPath: FieldPath.Managed(managed.StorageName, managed.ClrType));
            }
        }

        var members = new MemberInfo[path.Segments.Count];
        var currentType = rootType;

        for (var i = 0; i < path.Segments.Count; i++)
        {
            var segment = path.Segments[i];

            // Mid-path collection: dive into the element type before resolving the next segment.
            var elementType = TryGetElementType(currentType);
            if (elementType is not null && i > 0) currentType = elementType;

            var prop = FindProperty(currentType, segment, path, rootType);
            if (prop is null && elementType is not null)
            {
                prop = FindProperty(elementType, segment, path, rootType);
                if (prop is not null) currentType = elementType;
            }
            if (prop is null)
                throw new InvalidFilterFieldException(path.ToString(), rootType, segment);

            members[i] = prop;
            currentType = prop.PropertyType;
        }

        var leafType = currentType;
        var element = TryGetElementType(leafType);
        var targetsCollection = element is not null;
        var comparable = targetsCollection
            ? element!
            : Nullable.GetUnderlyingType(leafType) ?? leafType;

        return new ResolvedField(rootType, members, leafType, comparable, targetsCollection, element,
            CanonicalPath: FieldPath.Of(members.Select(member => member.Name).ToArray()));
    }

    private static PropertyInfo? FindProperty(Type type, string segment, FieldPath path, Type rootType)
    {
        var properties = type.GetProperties(Flags);
        var exact = properties.Where(property =>
            string.Equals(property.Name, segment, StringComparison.Ordinal)).ToArray();
        if (exact.Length == 1) return exact[0];
        if (exact.Length > 1)
            throw new InvalidFilterFieldException(path.ToString(), rootType, segment,
                "The exact member name is ambiguous on the CLR type.");

        var insensitive = properties.Where(property =>
            string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (insensitive.Length == 1) return insensitive[0];
        if (insensitive.Length > 1)
            throw new InvalidFilterFieldException(path.ToString(), rootType, segment,
                "Use the member's exact casing because this type declares multiple case-insensitive matches.");
        return null;
    }

    /// <summary>Element type when <paramref name="t"/> is a generic enumerable (excluding string), else null.</summary>
    internal static Type? TryGetElementType(Type t)
    {
        if (t == typeof(string)) return null;
        if (t.IsArray) return t.GetElementType();

        foreach (var iface in t.GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return t.GetGenericArguments()[0];

        if (typeof(IEnumerable).IsAssignableFrom(t) && t != typeof(string))
            return typeof(object);

        return null;
    }
}
