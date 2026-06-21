using System;

namespace Koan.Core.Composition;

/// <summary>
/// A pillar's contribution to the boot-time resolved composition twin (P1.1). Koan.Core writes the
/// kernel-knowable sections (app, modules, config keys); pillars that own runtime-resolved state
/// (e.g. Koan.Data owns adapter elections and entity configs) enrich the twin through this seam.
/// </summary>
/// <remarks>
/// Mark implementations discoverable by referencing the package — this interface is
/// <c>[KoanDiscoverable]</c>, so implementers are auto-registered into <c>KoanRegistry</c> and run at
/// boot with no manual wiring (Reference = Intent). Implementations are instantiated parameterlessly
/// and pull what they need from the supplied <see cref="IServiceProvider"/>. Contributions must be
/// best-effort and fail-soft: a throwing contributor must never break the boot report.
/// </remarks>
[KoanDiscoverable]
public interface IKoanCompositionContributor
{
    /// <summary>Enrich the resolved twin. Called once at boot after the service provider is built.</summary>
    void Contribute(KoanCompositionBuilder builder, IServiceProvider services);
}
