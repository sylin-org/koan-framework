using System.Net;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Secrets.Abstractions;
using Koan.Secrets.Connector.Vault.Internal;

namespace Koan.Secrets.Connector.Vault;

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
            using var resp = await client.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new SecretNotFoundException(id.ToString());
            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new SecretUnauthorizedException(id.ToString());
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(ct);
            var rootToken = JToken.Parse(json);
            if (_options.UseKvV2)
            {
                // Expect { data: { data: { ... }, metadata: { version, created_time } } }
                var rootData = rootToken["data"] as JObject;
                var data = rootData?["data"];
                if (rootData is null || data is null)
                    throw new SecretProviderUnavailableException("vault", "Unexpected KV v2 response");
                var meta = new SecretMetadata
                {
                    Version = rootData.TryGetValue("metadata", out var md) && md? ["version"] is JValue ver ? ver.Value<int>().ToString() : id.Version,
                    Provider = "vault",
                    Created = rootData.TryGetValue("metadata", out var md2) && md2? ["created_time"]?.Value<string>() is { } s && DateTimeOffset.TryParse(s, out var dto) ? dto : null,
                    Ttl = _options.DefaultTtl,
                };
                return MaterializeValue(data, meta);
            }
            else
            {
                // KV v1: arbitrary JSON payload
                var meta = new SecretMetadata { Provider = "vault", Version = id.Version, Ttl = _options.DefaultTtl };
                return MaterializeValue(rootToken, meta);
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

    private static SecretValue MaterializeValue(JToken payload, SecretMetadata meta)
    {
        // Prefer a 'value' property if present and is string
        if (payload.Type == JTokenType.Object)
        {
            if (payload["value"] is JValue v && v.Type == JTokenType.String)
            {
                return new SecretValue(System.Text.Encoding.UTF8.GetBytes(v.Value<string>()!), SecretContentType.Text, meta);
            }
            // If single string field, use it
            if (payload is JObject obj)
            {
                if (obj.Properties().Count() == 1 && obj.Properties().First().Value is JValue sv && sv.Type == JTokenType.String)
                {
                    return new SecretValue(System.Text.Encoding.UTF8.GetBytes(sv.Value<string>()!), SecretContentType.Text, meta);
                }
            }
            // If 'bytes' exists and is base64
            if (payload["bytes"] is JValue b && b.Type == JTokenType.String)
            {
                var raw = Convert.FromBase64String(b.Value<string>()!);
                return new SecretValue(raw, SecretContentType.Bytes, meta);
            }
            // Fallback: return JSON as-is
            var json = payload.ToString(Newtonsoft.Json.Formatting.None);
            return new SecretValue(System.Text.Encoding.UTF8.GetBytes(json), SecretContentType.Json, meta);
        }
        // Non-object: return raw text
        var text = payload.ToString(Newtonsoft.Json.Formatting.None);
        return new SecretValue(System.Text.Encoding.UTF8.GetBytes(text), SecretContentType.Text, meta);
    }
}

