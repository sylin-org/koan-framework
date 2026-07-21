using System;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Abstractions.Policies;

/// <summary>
/// Entity-friendly shortcut for <see cref="CachePolicyAttribute"/>. Defaults match the 90% case:
/// 300s TTL, Layered tier, GetOrSet strategy, canonical key template
/// <c>"{TypeName}:{Partition}:{Id}"</c>, and a single tag <c>"{TypeName}"</c> so
/// <c>EntityType.Cache.Flush()</c> works out of the box.
/// </summary>
/// <remarks>
/// <para>
/// Discovered by the same policy bootstrapper that scans <see cref="CachePolicyAttribute"/> —
/// no extra wiring. Power-user scenarios (controller actions, method scope, custom key templates,
/// multiple policies per type) drop to <c>[CachePolicy]</c> directly.
/// </para>
/// <para>
/// Integer-second setters bridge the gap that C# attribute syntax can't pass <c>TimeSpan</c>
/// literals: <c>[Cacheable(60, L1TtlSeconds = 10, SlidingTtlSeconds = 30)]</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true, AllowMultiple = false)]
public class CacheableAttribute : CachePolicyAttribute
{
    /// <summary>The canonical entity key template used by all <see cref="CacheableAttribute"/> instances.</summary>
    public const string DefaultKeyTemplate = "{TypeName}:{Partition}:{Id}";

    /// <summary>The sentinel tag token resolved to <c>typeof(T).Name</c> at policy materialization.</summary>
    public const string TypeNameTagToken = "{TypeName}";

    /// <param name="ttlSeconds">
    /// Absolute TTL in seconds. <c>0</c> = no expiration. Default <c>300</c> (5 minutes).
    /// Throws if negative.
    /// </param>
    public CacheableAttribute(int ttlSeconds = 300)
        : base(CacheScope.Entity, DefaultKeyTemplate)
    {
        if (ttlSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(ttlSeconds), "TTL seconds must be non-negative.");

        if (ttlSeconds > 0) AbsoluteTtl = TimeSpan.FromSeconds(ttlSeconds);

        Tier = CacheTier.Layered;
        Strategy = CacheStrategy.GetOrSet;
        Tags = new[] { TypeNameTagToken };
    }

    /// <summary>L1-specific TTL in seconds. <c>0</c> = same as L2. When unset, L1 derives <c>max(30, L2Ttl/2)</c>.</summary>
    /// <remarks>
    /// A read-write projection of the canonical <see cref="CachePolicyAttribute.L1AbsoluteTtl"/> (no duplicate
    /// state). The getter exists so this can be used as a <b>named attribute argument</b>
    /// (<c>[Cacheable(60, L1TtlSeconds = 10)]</c>) — C# requires named arguments to be read-write (CS0617).
    /// </remarks>
    public int L1TtlSeconds
    {
        get => L1AbsoluteTtl is { } ttl ? (int)ttl.TotalSeconds : 0;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "L1 TTL seconds must be non-negative.");
            L1AbsoluteTtl = value > 0 ? TimeSpan.FromSeconds(value) : null;
        }
    }

    /// <summary>Sliding TTL in seconds. Refreshed on each read when supported by the store.</summary>
    /// <remarks>Read-write projection of <see cref="CachePolicyAttribute.SlidingTtl"/> (see <see cref="L1TtlSeconds"/>).</remarks>
    public int SlidingTtlSeconds
    {
        get => SlidingTtl is { } ttl ? (int)ttl.TotalSeconds : 0;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Sliding TTL seconds must be non-negative.");
            SlidingTtl = value > 0 ? TimeSpan.FromSeconds(value) : null;
        }
    }

    /// <summary>Maximum bounded window in which callers that opt in may receive an expired value.</summary>
    /// <remarks>Read-write projection of <see cref="CachePolicyAttribute.AllowStaleFor"/> (see <see cref="L1TtlSeconds"/>).</remarks>
    public int AllowStaleForSeconds
    {
        get => AllowStaleFor is { } ttl ? (int)ttl.TotalSeconds : 0;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "AllowStaleFor seconds must be non-negative.");
            AllowStaleFor = value > 0 ? TimeSpan.FromSeconds(value) : null;
        }
    }
}
