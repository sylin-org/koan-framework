namespace Koan.Tenancy;

/// <summary>
/// A configured tenant resolution strategy (ARCH-0099 §1) — resolves the ambient tenant for an inbound request
/// from a trusted signal (a verified claim, the host/domain, or a header authorized against memberships).
/// Concrete resolvers land in a later slice; for now this is the seam the production boot pre-flight checks.
///
/// <para>Tenancy active in <b>Production</b> with no registered <see cref="ITenantResolver"/> <b>refuses to
/// boot</b> (every tenant-scoped op would otherwise fail closed at the gate — a fail-fast, not a silent leak).
/// In Development the auto-seeded dev tenant stands in, so a resolver is not required (dev-open, ARCH-0099 §1).</para>
/// </summary>
public interface ITenantResolver
{
    /// <summary>A short, stable name for diagnostics and the boot report (e.g. <c>"claim"</c>, <c>"host"</c>, <c>"header"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Resolve a <b>candidate</b> tenant id from the inbound <paramref name="request"/>, or null when this resolver's
    /// carrier is not present on the request. The caller (the web pipeline middleware) authorizes the candidate
    /// against the subject's memberships before scoping — a resolver returns the <i>signalled</i> tenant, it does not
    /// grant access. The default no-op keeps the bare boot-marker pattern (a presence-only resolver) compiling.
    /// </summary>
    ValueTask<string?> ResolveAsync(TenantResolutionRequest request, CancellationToken ct = default)
        => new((string?)null);
}
