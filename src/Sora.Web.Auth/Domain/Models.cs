namespace Sora.Web.Auth.Domain;

public sealed class ExternalIdentity
{
    public required string UserId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderKeyHash { get; init; }
    public string? ClaimsJson { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CurrentUserDto
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? PictureUrl { get; init; }
    public IReadOnlyList<ConnectionDto> Connections { get; init; } = Array.Empty<ConnectionDto>();
}

public sealed class ConnectionDto
{
    public required string Provider { get; init; }
    public required string DisplayName { get; init; }
    public required string KeyHash { get; init; }
}
