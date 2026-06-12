using System.Collections;
using System.Reflection;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Core.Sorting;

/// <summary>
/// Resolves dot-path strings into structured <see cref="MemberPath"/> instances. Strict by default —
/// unresolvable segments throw <see cref="InvalidSortFieldException"/>.
/// </summary>
public static class MemberPathResolver
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

    /// <summary>
    /// Resolves a dot-path against <typeparamref name="T"/>. Throws if any segment fails to resolve.
    /// </summary>
    public static MemberPath ResolveStrict<T>(string field) => ResolveStrict(typeof(T), field);

    /// <summary>
    /// Resolves a dot-path against the given root type. Throws if any segment fails to resolve.
    /// </summary>
    public static MemberPath ResolveStrict(Type rootType, string field)
    {
        if (rootType is null) throw new ArgumentNullException(nameof(rootType));
        if (string.IsNullOrWhiteSpace(field))
            throw new InvalidSortFieldException(field ?? string.Empty, rootType, string.Empty, "Field is empty.");

        var segments = field.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            throw new InvalidSortFieldException(field, rootType, string.Empty, "Field is empty after trim.");

        var members = new MemberInfo[segments.Length];
        var currentType = rootType;
        var traversesCollection = false;
        var collectionIdx = -1;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            // Detect collection at the current level: dive into element type before resolving the next segment.
            // We only do this when the current type is a collection AND there is a property to look up on its element.
            var elementType = TryGetCollectionElementType(currentType);
            if (elementType is not null && i > 0)
            {
                traversesCollection = true;
                if (collectionIdx < 0) collectionIdx = i;
                currentType = elementType;
            }

            var prop = currentType.GetProperty(segment, Flags);
            if (prop is null)
            {
                // Last-chance: maybe segment is meant against the collection element type even on the first hop.
                if (elementType is not null)
                {
                    prop = elementType.GetProperty(segment, Flags);
                    if (prop is not null)
                    {
                        traversesCollection = true;
                        if (collectionIdx < 0) collectionIdx = i;
                        currentType = elementType;
                    }
                }
                if (prop is null)
                {
                    throw new InvalidSortFieldException(field, rootType, segment);
                }
            }

            members[i] = prop;
            currentType = prop.PropertyType;
        }

        // After the final segment, check whether the leaf itself is a collection (rare for sort, but supported by aggregation).
        // We do NOT change ValueType when leaf is scalar (the common case).
        var leafValueType = currentType;
        var leafElement = TryGetCollectionElementType(currentType);
        if (leafElement is not null)
        {
            traversesCollection = true;
            if (collectionIdx < 0) collectionIdx = segments.Length - 1;
            leafValueType = leafElement;
        }

        return new MemberPath(rootType, members, leafValueType, traversesCollection, collectionIdx);
    }

    /// <summary>
    /// Returns the element type when <paramref name="t"/> is a generic <see cref="IEnumerable{T}"/> (excluding string),
    /// otherwise null.
    /// </summary>
    internal static Type? TryGetCollectionElementType(Type t)
    {
        if (t == typeof(string)) return null;
        if (t.IsArray) return t.GetElementType();

        foreach (var iface in t.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return t.GetGenericArguments()[0];

        if (typeof(IEnumerable).IsAssignableFrom(t) && t != typeof(string))
            return typeof(object);

        return null;
    }
}
