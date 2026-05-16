namespace Koan.Secrets.Abstractions;

public interface ISecretProvider
{
    Task<SecretValue> Get(SecretId id, CancellationToken ct = default);
}