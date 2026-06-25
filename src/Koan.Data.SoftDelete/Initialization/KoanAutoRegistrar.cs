using Microsoft.Extensions.DependencyInjection;
using Koan.Core;

namespace Koan.Data.SoftDelete.Initialization;

/// <summary>
/// Lights up soft-delete when <c>Koan.Data.SoftDelete</c> is referenced (Reference = Intent). The three composition
/// seams — the invisible <c>__deleted</c> managed field, the hide-deleted read contributor, and the
/// <c>Delete ⇒ __deleted=true</c> operation override — are now declared in ONE place, <see cref="SoftDeleteAxis"/>
/// (a <c>[KoanDiscoverable]</c> <c>IDataAxis</c>), and expanded byte-identically by <c>DataAxisExpander</c> at boot
/// (ARCH-0102 / ARCH-0101 §7 — the authoring shrink). This module remains as the boot-report marker; it registers no
/// DI of its own. The per-entity opt-in is <see cref="SoftDeleteAttribute"/>.
/// </summary>
public sealed class KoanAutoRegistrar : KoanModule
{
    public override string Id => "Koan.Data.SoftDelete";

    public override void Register(IServiceCollection services) { }
}
