using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Cache.Abstractions.Policies;

namespace Koan.Cache.Policies;

/// <summary>
/// Materializes a <see cref="CachePolicyAttribute"/> into a runtime <see cref="CachePolicyDescriptor"/>.
/// Resolves the <c>{TypeName}</c> sentinel in <c>Tags</c>, derives a defense-in-depth default
/// for <c>L1AbsoluteTtl</c> when unset, and validates the resulting descriptor.
/// </summary>
internal static class CachePolicyMaterializer
{
    /// <summary>Sentinel token replaced with <c>declaringType.Name</c> at materialization time.</summary>
    public const string TypeNameTagToken = "{TypeName}";

    /// <summary>Floor for derived L1 TTL — caps worst-case staleness when coherence is silent.</summary>
    /// <remarks>Re-exposes <see cref="Abstractions.Policies.CacheL1TtlPolicy.DefaultFloor"/> at this
    /// type's surface so existing call sites keep compiling; the rule itself lives in the policy.</remarks>
    public static readonly TimeSpan L1TtlFloor = Abstractions.Policies.CacheL1TtlPolicy.DefaultFloor;

    public static CachePolicyDescriptor Materialize(CachePolicyAttribute attribute, MemberInfo? member, Type? declaringType)
    {
        if (attribute is null) throw new ArgumentNullException(nameof(attribute));

        var typeName = declaringType?.Name ?? "Unknown";

        var tags = ResolveTags(attribute.Tags, typeName);
        var metadata = attribute.Metadata is null || attribute.Metadata.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : attribute.Metadata.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal);

        var l1Ttl = ResolveL1Ttl(attribute.AbsoluteTtl, attribute.L1AbsoluteTtl);
        ValidateTtl(typeName, attribute.AbsoluteTtl, l1Ttl);

        return new CachePolicyDescriptor(
            Scope: attribute.Scope,
            KeyTemplate: attribute.KeyTemplate,
            Strategy: attribute.Strategy,
            Tier: attribute.Tier,
            AbsoluteTtl: attribute.AbsoluteTtl,
            L1AbsoluteTtl: l1Ttl,
            SlidingTtl: attribute.SlidingTtl,
            AllowStaleFor: attribute.AllowStaleFor,
            Tags: tags,
            Region: attribute.Region,
            ScopeId: attribute.ScopeId,
            ForceCoherenceBroadcast: attribute.ForceCoherenceBroadcast,
            Metadata: metadata,
            TargetMember: member,
            DeclaringType: declaringType);
    }

    /// <summary>
    /// Resolve tag tokens. Currently only <c>{TypeName}</c> is interpolated; other dynamic
    /// tokens (e.g. <c>{TenantId}</c>) are deferred to runtime tag-flush callers who carry
    /// the ambient values.
    /// </summary>
    private static IReadOnlyList<string> ResolveTags(string[]? raw, string typeName)
    {
        if (raw is null || raw.Length == 0) return Array.Empty<string>();

        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in raw)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var trimmed = tag.Trim();
            if (string.Equals(trimmed, TypeNameTagToken, StringComparison.Ordinal))
                resolved.Add(typeName);
            else
                resolved.Add(trimmed);
        }

        return resolved.Count == 0 ? Array.Empty<string>() : resolved.ToArray();
    }

    /// <summary>
    /// Compute the effective L1 TTL. Delegates to
    /// <see cref="Abstractions.Policies.CacheL1TtlPolicy.Derive(TimeSpan?, TimeSpan?)"/> — the
    /// single source of truth shared with the per-write path so the rule can't drift between
    /// call sites.
    /// </summary>
    public static TimeSpan? ResolveL1Ttl(TimeSpan? absoluteTtl, TimeSpan? l1Override)
        => Abstractions.Policies.CacheL1TtlPolicy.Derive(absoluteTtl, l1Override);

    private static void ValidateTtl(string typeName, TimeSpan? absoluteTtl, TimeSpan? l1Ttl)
    {
        if (absoluteTtl is { } absolute && l1Ttl is { } l1 && l1 > absolute)
        {
            throw new InvalidOperationException(
                $"Cache policy on '{typeName}': L1AbsoluteTtl ({l1}) cannot exceed AbsoluteTtl ({absolute}). " +
                "L1 must expire no later than L2 to preserve the defense-in-depth invariant.");
        }
    }
}
