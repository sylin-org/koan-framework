using Koan.Data.Abstractions;

namespace Koan.Tenancy;

/// <summary>
/// Maintenance runs in the tenant already in effect, if one is, which may be in no registry (the Open posture's local
/// tenant, a tenant named by a trusted caller), and then once per other tenant in the registry, read a page at a time.
/// </summary>
internal sealed class TenantMaintenanceScopes : IMaintenanceScopes
{
    private const int PageSize = 200;

    public async Task ForEach(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        var inEffect = TenancyAmbient.EffectiveTenantId();
        if (inEffect is { Length: > 0 }) await work(ct).ConfigureAwait(false);
        for (var page = 1; ; page++)
        {
            var tenants = await TenantRecord.Page(page, PageSize, ct).ConfigureAwait(false);
            foreach (var tenant in tenants.Where(tenant => tenant.Id != inEffect))
                using (Tenant.Use(tenant.Id))
                    await work(ct).ConfigureAwait(false);
            if (tenants.Count < PageSize) return;
        }
    }
}
