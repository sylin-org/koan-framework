using System;
using Koan.Data.Core;

namespace Koan.Tenancy;

/// <summary>
/// ARCH-0100: the tenancy <see cref="IAmbientSliceCarrier"/> — carries the ambient <see cref="TenantContext"/>
/// across a durable async-hop so a background job (later: a message) runs in the tenant it was submitted under.
/// Both halves already exist: capture reads the tri-state slice, restore reuses <see cref="Tenant.Use"/> /
/// <see cref="Tenant.None"/> (which return the slice-restore scope). The captured string is <b>versioned</b> so a
/// future format change fails closed at restore (dead-letter) rather than mis-restoring into a wrong/ghost tenant.
///
/// <para>Capture reads only the <i>explicit</i> ambient slice: an unset scope captures nothing (the bag is absent),
/// so the request-path dev-fallback is not made portable — at execute, an absent bag restores nothing and the
/// request-path guard (ARCH-0099 §1b) owns the refusal (Closed) / dev-fallback (Open).</para>
/// </summary>
public sealed class TenantContextCarrier : IAmbientSliceCarrier
{
    private const string Version = "v1";
    private const string HostToken = "v1:host";
    private const string IdPrefix = "v1:id:";

    public string AxisKey => "koan:tenant";

    public string? Capture()
    {
        var slice = Tenant.Current;
        if (slice is null) return null;                          // unset → nothing to carry
        return slice.IsHost ? HostToken : IdPrefix + slice.Id;   // explicit host vs concrete tenant
    }

    public IDisposable Restore(string captured)
    {
        if (captured == HostToken) return Tenant.None();
        if (captured.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            var id = captured.Substring(IdPrefix.Length);
            if (id.Length == 0)
                throw new InvalidOperationException("TenantContextCarrier: the captured tenant id is empty — refusing to restore.");
            return Tenant.Use(id);
        }
        // Unknown/future format — fail closed (named) so the orchestrator dead-letters rather than mis-restoring.
        throw new InvalidOperationException(
            $"TenantContextCarrier: cannot restore tenant from an unknown captured format '{captured}' (expected version '{Version}').");
    }

    // Explicitly clear the ambient tenant for this scope — so a job submitted with no tenant never inherits the
    // worker/inline-drain thread's tenant. Tenant.Current becomes null (unset); the §1b guard then owns the
    // refusal (Closed) / dev-fallback (Open), exactly as for a request with no tenant.
    public IDisposable Suppress() => EntityContext.WithSlice<TenantContext>(null);
}
