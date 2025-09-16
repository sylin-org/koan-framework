namespace Koan.Secrets.Abstractions;

public interface ISecretProvider
{
    Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default);
}