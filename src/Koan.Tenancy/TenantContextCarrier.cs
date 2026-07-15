using System;
using Koan.Core.Context;
using Koan.Tenancy.Infrastructure;

namespace Koan.Tenancy;

/// <summary>
/// Carries the ambient <see cref="TenantContext"/> across a durable hop so work runs in the tenant under which it
/// was submitted. The opaque wire form is versioned and restoration requires authenticated ingress provenance.
/// Malformed or unsupported data fails with a safe typed error before any tenant scope is pushed.
///
/// <para>Capture reads only the <i>explicit</i> context value: an unset scope captures nothing (the bag is absent),
/// so the request-path dev-fallback is not made portable. At execute, an absent bag explicitly suppresses tenant
/// context and the request-path guard (ARCH-0099 §1b) owns the refusal (Closed) / dev-fallback (Open).</para>
/// </summary>
public sealed class TenantContextCarrier : IKoanContextCarrier
{
    /// <inheritdoc />
    public string AxisKey => Constants.ContextCarriage.AxisKey;

    /// <inheritdoc />
    public ContextIngressTrust MinimumIngressTrust => ContextIngressTrust.Authenticated;

    public string? Capture()
    {
        var slice = Tenant.Current;
        if (slice is null) return null;                          // unset → nothing to carry
        return slice.IsHost
            ? Constants.ContextCarriage.HostToken
            : Constants.ContextCarriage.IdPrefix + slice.Id;   // explicit host vs concrete tenant
    }

    /// <inheritdoc />
    public IDisposable Restore(string captured)
    {
        if (captured is null)
            throw KoanContextCarrierException.MalformedPayload(AxisKey);

        if (captured == Constants.ContextCarriage.HostToken) return Tenant.None();
        if (captured.StartsWith(Constants.ContextCarriage.IdPrefix, StringComparison.Ordinal))
        {
            var id = captured[Constants.ContextCarriage.IdPrefix.Length..];
            if (string.IsNullOrWhiteSpace(id))
                throw KoanContextCarrierException.MalformedPayload(AxisKey);
            return Tenant.Use(id);
        }

        throw captured.StartsWith(Constants.ContextCarriage.VersionPrefix, StringComparison.Ordinal)
            ? KoanContextCarrierException.MalformedPayload(AxisKey)
            : KoanContextCarrierException.UnsupportedVersion(AxisKey);
    }

    /// <inheritdoc />
    public IDisposable Suppress() => KoanContext.Suppress<TenantContext>();
}
