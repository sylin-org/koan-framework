namespace Koan.Identity.Tenancy;

/// <summary>
/// Resolves an inbound carrier to a candidate tenant id. A resolver never grants access: the Identity Tenancy
/// request chokepoint still requires an active durable Identity and matching Membership before establishing scope.
/// </summary>
public interface ITenantResolver
{
    /// <summary>A short stable name used in runtime explanation.</summary>
    string Name { get; }

    /// <summary>Return the candidate tenant id, or <c>null</c> when this carrier is absent.</summary>
    ValueTask<string?> ResolveAsync(TenantResolutionRequest request, CancellationToken ct = default)
        => new((string?)null);
}
