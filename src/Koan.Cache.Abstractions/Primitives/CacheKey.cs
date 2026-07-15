using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Koan.Cache.Abstractions.Primitives;

public readonly record struct CacheKey
{
    public CacheKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Cache key cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static implicit operator string(CacheKey key) => key.Value;

    public static implicit operator CacheKey(string value) => new(value);

    public override string ToString() => Value;

    public bool Equals(CacheKey other)
        => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public bool Matches(string value)
        => string.Equals(Value, value, StringComparison.Ordinal);

    public CacheKey WithPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Prefix cannot be null or whitespace.", nameof(prefix));
        }

        return new CacheKey(prefix + Value);
    }

    public CacheKey WithSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            throw new ArgumentException("Suffix cannot be null or whitespace.", nameof(suffix));
        }

        return new CacheKey(Value + suffix);
    }

    public bool StartsWith(ReadOnlySpan<char> prefix)
        => Value.AsSpan().StartsWith(prefix, StringComparison.Ordinal);

    public bool EndsWith(ReadOnlySpan<char> suffix)
        => Value.AsSpan().EndsWith(suffix, StringComparison.Ordinal);

    public static CacheKey Concat(CacheKey left, CacheKey right)
        => new(left.Value + right.Value);

    public static bool TryParse(string? value, [NotNullWhen(true)] out CacheKey key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key = default;
            return false;
        }

        key = new CacheKey(value.Trim());
        return true;
    }

    /// <summary>
    /// Build the canonical entity cache key <c>"{TypeName}:{Partition}:{Id}"</c> used by
    /// <c>[Cacheable]</c>. Eliminates stringly-typed construction at out-of-band evict sites.
    /// Null/whitespace partition is rendered as the <c>"_"</c> sentinel.
    /// </summary>
    public static CacheKey For<TEntity>(object id, string? partition = null)
        => For(typeof(TEntity), id, partition);

    /// <summary>
    /// Build the canonical entity cache key for <paramref name="entityType"/>. Non-generic form.
    /// </summary>
    /// <remarks>
    /// The tokens are rendered to byte-match the read path's template (<c>CacheKeyTemplate</c>), so an out-of-band
    /// evict hits the same entry the read path cached (redesign gap B): the partition is taken <b>verbatim</b> (NOT
    /// trimmed — the template stores <c>EntityContext.Partition</c> raw, and two distinct partitions must never
    /// collapse to one cache key), and the id is rendered <b>culture-invariantly</b> for an
    /// <see cref="IFormattable"/> key (matching the template's invariant rendering) so a negative-int / DateTime key
    /// keys identically under any process culture.
    /// </remarks>
    public static CacheKey For(Type entityType, object id, string? partition = null)
    {
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        if (id is null) throw new ArgumentNullException(nameof(id));

        var typeName = EntityTypeName(entityType);
        var partitionToken = string.IsNullOrWhiteSpace(partition) ? "_" : partition;
        var idToken = (id is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : id.ToString())
            ?? throw new ArgumentException("Id.ToString() returned null.", nameof(id));

        return new CacheKey($"{typeName}:{partitionToken}:{idToken}");
    }

    /// <summary>
    /// Stable, collision-free type token for cache keys and tags. For a non-generic type this is
    /// <c>Type.Name</c> (unchanged). For a closed generic it strips the arity marker AND appends
    /// the (recursive) type-argument tokens — so <c>EmbeddingState&lt;Produce&gt;</c> and
    /// <c>EmbeddingState&lt;Other&gt;</c> don't both collapse to <c>EmbeddingState`1</c> (a silent cross-type
    /// cache collision) the way <c>Type.Name</c> does. Cache keys are ephemeral, so no migration is needed.
    /// (Stripping the arity alone is insufficient — it would still collide; the type args are what disambiguate.)
    /// </summary>
    public static string EntityTypeName(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (!type.IsGenericType) return type.Name;

        var name = type.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];

        var args = type.GetGenericArguments();
        var rendered = new string[args.Length];
        for (var i = 0; i < args.Length; i++) rendered[i] = EntityTypeName(args[i]);
        return name + "<" + string.Join(",", rendered) + ">";
    }
}
