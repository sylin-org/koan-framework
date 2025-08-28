using Microsoft.Extensions.Configuration;
using Sora.Secrets.Abstractions;

namespace Sora.Secrets.Core.Providers;

public sealed class ConfigurationSecretProvider(IConfiguration configuration) : ISecretProvider
{
    private readonly IConfiguration _cfg = configuration;

    public Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
    {
        // Map to Secrets:<scope>:<name> path
        var val = _cfg[$"Secrets:{id.Scope}:{id.Name}"];
        if (string.IsNullOrEmpty(val)) throw new SecretNotFoundException(id.ToString());
        return Task.FromResult(new SecretValue(System.Text.Encoding.UTF8.GetBytes(val), SecretContentType.Text, new SecretMetadata { Provider = "config" }));
    }
}
