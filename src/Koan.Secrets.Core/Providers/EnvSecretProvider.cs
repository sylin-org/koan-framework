using Koan.Secrets.Abstractions;

namespace Koan.Secrets.Core.Providers;

public sealed class EnvSecretProvider : ISecretProvider
{
    public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
    {
        // Convention: SCOPE__NAME or SCOPE_NAME (uppercased, non-alnum to underscore)
        var key = ($"{id.Scope}__{id.Name}").Replace('-', '_').Replace('/', '_').ToUpperInvariant();
        var val = Environment.GetEnvironmentVariable(key) ?? Environment.GetEnvironmentVariable($"{id.Scope}_{id.Name}".ToUpperInvariant());
        if (string.IsNullOrEmpty(val)) throw new SecretNotFoundException(id.ToString());
        return Task.FromResult(new SecretValue(System.Text.Encoding.UTF8.GetBytes(val), SecretContentType.Text, new SecretMetadata { Provider = "env" }));
    }
}
