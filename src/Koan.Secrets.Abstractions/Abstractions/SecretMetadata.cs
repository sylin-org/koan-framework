namespace Koan.Secrets.Abstractions;

public sealed class SecretMetadata
{
    public string? Version { get; init; }
    public DateTimeOffset? Created { get; init; }
    public TimeSpan? Ttl { get; init; }
    public string? Provider { get; init; }
}