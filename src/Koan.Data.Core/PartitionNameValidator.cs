using System;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Core;

/// <summary>
/// Validates partition names so distinct partitions can never collide after identifier sanitization.
/// <para>
/// Adapters turn a raw partition into an identifier via
/// <see cref="Koan.Data.Abstractions.Naming.PartitionTokenPolicy"/>, which maps <b>every</b> disallowed
/// character to the same replacement (<c>'_'</c>). That is lossy: <c>"tenant/7"</c>, <c>"tenant 7"</c>,
/// and <c>"tenant_7"</c> all collapse to <c>"tenant_7"</c> — three logically-distinct partitions sharing
/// one physical store (silent cross-partition data bleed). This validator is the front door that rejects
/// exactly those lossy names so the mapping stays injective.
/// </para>
/// <para>
/// A name is valid iff sanitization would be a <b>no-op</b> — it already contains only letters, digits, or
/// an allowed separator (<c>-</c> <c>.</c> <c>_</c>) — OR it is a GUID (normalized injectively by the token
/// policy). The character set is kept in sync with <see cref="Koan.Data.Abstractions.Naming.PartitionTokenPolicy.AllowedExtraChars"/>.
/// </para>
/// <para>
/// Valid: <c>"archive"</c>, <c>"cold-tier"</c>, <c>"backup.v2"</c>, <c>"tenant_7"</c>, <c>"A"</c>,
/// <c>"prod-us-east-1"</c>, numeric ids like <c>"42"</c>, and GUIDs.<br/>
/// Invalid: <c>"tenant/7"</c>, <c>"a b"</c>, <c>"$scope"</c>, <c>"%x%"</c>, empty / whitespace.
/// </para>
/// <para>
/// Residual (not covered here): adapters that fold case (e.g. Couchbase lowercases scope names) can still
/// collide <c>"Tenant"</c> with <c>"tenant"</c>; that is an adapter-specific concern, narrower than the
/// cross-adapter lossy-replacement vector this validator closes.
/// </para>
/// </summary>
internal static class PartitionNameValidator
{
    /// <summary>
    /// Validate a partition name against the collision-safety rule. Delegates to the ONE shared injectivity rule
    /// (<see cref="PartitionTokenPolicy.IsInjective"/>) so the partition front-door and the ARCH-0101 §3 container-name
    /// particle plane stay in lock-step (conformity — one rule, no drift). Default policy: GUID or only
    /// letters/digits/<c>-._</c> (case-fold is a documented adapter-specific residual, see the class remarks).
    /// </summary>
    public static bool IsValid(string? name) => PartitionTokenPolicy.Default.IsInjective(name);
}
