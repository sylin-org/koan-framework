using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Secrets.Abstractions;
using Sora.Secrets.Vault.Internal;

namespace Sora.Secrets.Vault;

public sealed class VaultSecretProvider : ISecretProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VaultOptions _options;
    private readonly ILogger<VaultSecretProvider>? _logger;
    private readonly bool _disabled;

    public VaultSecretProvider(IServiceProvider sp, IHttpClientFactory httpClientFactory, IOptions<VaultOptions> options, ILogger<VaultSecretProvider>? logger = null, bool disabled = false)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _disabled = disabled;
    }

    public async Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default)
    {
        // Respect provider intent if present
        if (id.Provider is { Length: > 0 } && !string.Equals(id.Provider, "vault", StringComparison.OrdinalIgnoreCase))
            throw new SecretNotFoundException(id.ToString());

        if (_disabled || !_options.Enabled)
            throw new SecretNotFoundException(id.ToString());

        if (_options.Address is null || string.IsNullOrWhiteSpace(_options.Token))
            throw new SecretProviderUnavailableException("vault", "Address/Token not configured");

        var path = BuildPath(id);
        var client = _httpClientFactory.CreateClient(VaultConstants.HttpClientName);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new SecretNotFoundException(id.ToString());
            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new SecretUnauthorizedException(id.ToString());
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (_options.UseKvV2)
            {
                // Expect { data: { data: { ... }, metadata: { version, created_time } } }
                if (!doc.RootElement.TryGetProperty("data", out var rootData) || !rootData.TryGetProperty("data", out var data))
                    throw new SecretProviderUnavailableException("vault", "Unexpected KV v2 response");
                var meta = new SecretMetadata
                {
                    Version = rootData.TryGetProperty("metadata", out var md) && md.TryGetProperty("version", out var ver) ? ver.GetInt32().ToString() : id.Version,
                    Provider = "vault",
                    Created = rootData.TryGetProperty("metadata", out var md2) && md2.TryGetProperty("created_time", out var ctok) && ctok.GetString() is { } s && DateTimeOffset.TryParse(s, out var dto) ? dto : null,
                    Ttl = _options.DefaultTtl,
                };
                return MaterializeValue(data, meta);
            }
            else
            {
                // KV v1: arbitrary JSON payload
                var meta = new SecretMetadata { Provider = "vault", Version = id.Version, Ttl = _options.DefaultTtl };
                return MaterializeValue(doc.RootElement, meta);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Vault HTTP error for {Secret}", id.ToString());
            throw new SecretProviderUnavailableException("vault", ex.Message);
        }
    }

    private string BuildPath(SecretId id)
    {
        var basePath = _options.UseKvV2 ? $"v1/{_options.Mount}/data/{id.Scope}/{id.Name}" : $"v1/{_options.Mount}/{id.Scope}/{id.Name}";
        if (!string.IsNullOrEmpty(id.Version) && _options.UseKvV2)
        {
            basePath += $"?version={Uri.EscapeDataString(id.Version)}";
        }
        return basePath;
    }

    private static SecretValue MaterializeValue(JsonElement payload, SecretMetadata meta)
    {
        // Prefer a 'value' property if present and is string
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
            {
                return new SecretValue(System.Text.Encoding.UTF8.GetBytes(v.GetString()!), SecretContentType.Text, meta);
            }
            // If single string field, use it
            var props = payload.EnumerateObject().ToArray();
            if (props.Length == 1 && props[0].Value.ValueKind == JsonValueKind.String)
            {
                return new SecretValue(System.Text.Encoding.UTF8.GetBytes(props[0].Value.GetString()!), SecretContentType.Text, meta);
            }
            // If 'bytes' exists and is base64
            if (payload.TryGetProperty("bytes", out var b) && b.ValueKind == JsonValueKind.String)
            {
                var raw = Convert.FromBase64String(b.GetString()!);
                return new SecretValue(raw, SecretContentType.Bytes, meta);
            }
            // Fallback: return JSON as-is
            var json = payload.GetRawText();
            return new SecretValue(System.Text.Encoding.UTF8.GetBytes(json), SecretContentType.Json, meta);
        }
        // Non-object: return raw text
        var text = payload.GetRawText();
        return new SecretValue(System.Text.Encoding.UTF8.GetBytes(text), SecretContentType.Text, meta);
    }
}
