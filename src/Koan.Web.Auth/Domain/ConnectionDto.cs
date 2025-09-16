namespace Koan.Web.Auth.Domain;

public sealed class ConnectionDto
{
    public required string Provider { get; init; }
    public required string DisplayName { get; init; }
    public required string KeyHash { get; init; }
}