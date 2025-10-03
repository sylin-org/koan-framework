using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Canon.Model;

// Identity map link for a Canon model: (system, adapter, externalId) -> ReferenceId
public sealed class IdentityLink<TModel> : Entity<IdentityLink<TModel>>
{
    // Composite key string "{system}|{adapter}|{externalId}" to allow O(1) gets without provider-specific queries
    // Id acts as the primary key

    [Index]
    public string System { get; set; } = default!;
    [Index]
    public string Adapter { get; set; } = default!;
    [Index]
    public string ExternalId { get; set; } = default!;
    [Index]
    public string ReferenceId { get; set; } = default!;
    // Optional: carry business key for convenience; ReferenceId continues to carry Canonical UUID
    [Index]
    public string? CanonicalId { get; set; }

    public bool Provisional { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}

