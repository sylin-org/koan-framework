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
}
