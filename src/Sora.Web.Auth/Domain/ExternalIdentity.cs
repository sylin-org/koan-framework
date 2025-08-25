namespace Sora.Web.Auth.Domain;

public sealed class ExternalIdentity
{
    public required string UserId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderKeyHash { get; init; }
    public string? ClaimsJson { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}