using System;

namespace Koan.Tenancy;

/// <summary>
/// Composes the fail-closed gate's refusal diagnostic (ARCH-0099 §1) — Redis protected-mode quality, not a bare
/// 403: it states what was refused and why, then names the <b>exact</b> remediations (scope the call, register a
/// resolver, or exempt the entity), and reminds the developer that Development is auto-open. Pure so the content
/// is unit-testable. The opening phrase stays stable for callers/tests that match on it.
/// </summary>
public static class TenancyRefusal
{
    public static string NoTenantInScope(string entityName)
    {
        var nl = Environment.NewLine;
        return
            $"No tenant in scope for tenant-scoped '{entityName}' — the posture is Closed, so tenancy fails closed " +
            "(ARCH-0099 §1). This is a guard, not a bug: without a tenant the operation could span every tenant. " +
            "To proceed, do one of:" + nl +
            $"  1) Scope the call:        using (Tenant.Use(\"<tenantId>\")) {{ ... }}   (admin / jobs / tests)" + nl +
            "  2) Resolve automatically:  register an ITenantResolver (claim / host / header) so requests carry a tenant." + nl +
            $"  3) Exempt system data:     mark '{entityName}' [HostScoped] if it is genuinely control-plane data (no tenant)." + nl +
            "  (In Development the posture is Open and a dev tenant is auto-seeded — you would not see this there.)";
    }
}
