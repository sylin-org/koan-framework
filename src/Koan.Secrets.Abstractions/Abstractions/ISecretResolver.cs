namespace Koan.Secrets.Abstractions;

public interface ISecretResolver
{
    Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default);
    Task<string> ResolveAsync(string template, CancellationToken ct = default);
}