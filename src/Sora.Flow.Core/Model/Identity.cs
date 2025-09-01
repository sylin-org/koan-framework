using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;

namespace Sora.Flow.Model;

// Identity map link for a Flow model: (system, adapter, externalId) -> ReferenceUlid
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
    public string ReferenceUlid { get; set; } = default!;
    // Optional: carry business key for convenience; ReferenceId continues to carry canonical ULID
    [Index]
    public string? CanonicalId { get; set; }

    public bool Provisional { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}
