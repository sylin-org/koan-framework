using Koan.Core.Diagnostics;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>Host-owned handoff from pre-DI module activation into runtime explanation.</summary>
internal sealed record KoanBootstrapSnapshot(
    RegistrySummarySnapshot Registry,
    IReadOnlyList<KoanFact> Facts);
