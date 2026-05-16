namespace Koan.Secrets.Abstractions;

public interface ISecretResolver
{
    Task<SecretValue> Get(SecretId id, CancellationToken ct = default);
    Task<string> Resolve(string template, CancellationToken ct = default);
}