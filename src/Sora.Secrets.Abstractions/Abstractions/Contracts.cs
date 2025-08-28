using Newtonsoft.Json;
namespace Sora.Secrets.Abstractions;

public interface ISecretProvider
{
    Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default);
}

public interface ISecretResolver
{
    Task<SecretValue> GetAsync(SecretId id, CancellationToken ct = default);
    Task<string> ResolveAsync(string template, CancellationToken ct = default);
}

public readonly record struct SecretId(string Scope, string Name, string? Version = null, string? Provider = null)
{
    public static SecretId Parse(string uri)
    {
        // Accept forms: secret://scope/name?version=.. and secret+provider://scope/name
        if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("Empty secret id", nameof(uri));
        var u = new Uri(uri, UriKind.Absolute);
        var scheme = u.Scheme;
        string? provider = null;
        if (scheme.StartsWith("secret+", StringComparison.OrdinalIgnoreCase)) provider = scheme[7..];
        else if (!string.Equals(scheme, "secret", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Unsupported scheme: {scheme}");
        var host = u.Host;
        var segments = u.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string scope;
        string name;
        if (!string.IsNullOrEmpty(host))
        {
            scope = host;
            if (segments.Length < 1) throw new ArgumentException("Secret path must be scope/name", nameof(uri));
            name = segments[0];
        }
        else
        {
            if (segments.Length < 2) throw new ArgumentException("Secret path must be scope/name", nameof(uri));
            scope = segments[0];
            name = segments[1];
        }
        var version = System.Web.HttpUtility.ParseQueryString(u.Query).Get("version");
        return new SecretId(scope, name, version, provider);
    }

    public override string ToString() =>
        Provider is { Length: > 0 }
            ? $"secret+{Provider}://{Scope}/{Name}{(Version is { Length: > 0 } ? $"?version={Version}" : string.Empty)}"
            : $"secret://{Scope}/{Name}{(Version is { Length: > 0 } ? $"?version={Version}" : string.Empty)}";
}

public enum SecretContentType { Text, Bytes, Json }

public sealed class SecretMetadata
{
    public string? Version { get; init; }
    public DateTimeOffset? Created { get; init; }
    public TimeSpan? Ttl { get; init; }
    public string? Provider { get; init; }
}

public sealed class SecretValue
{
    private readonly byte[] _data;
    public SecretContentType Type { get; }
    public SecretMetadata Meta { get; }

    public SecretValue(byte[] data, SecretContentType type, SecretMetadata? meta = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Type = type;
        Meta = meta ?? new SecretMetadata();
    }

    public ReadOnlyMemory<byte> AsBytes() => _data;
    public string AsString() => Type switch
    {
        SecretContentType.Text or SecretContentType.Json => System.Text.Encoding.UTF8.GetString(_data),
        _ => throw new InvalidOperationException("Secret is not textual"),
    };

    public T AsJson<T>() => Type == SecretContentType.Json
        ? JsonConvert.DeserializeObject<T>(System.Text.Encoding.UTF8.GetString(_data))!
        : throw new InvalidOperationException("Secret is not JSON");

    public override string ToString() => "***"; // never expose
}

public class SecretException(string message) : Exception(message);
public sealed class SecretNotFoundException(string id) : SecretException($"Secret not found: {id}");
public sealed class SecretUnauthorizedException(string id) : SecretException($"Unauthorized to access secret: {id}");
public sealed class SecretProviderUnavailableException(string provider, string? reason = null)
    : SecretException($"Secret provider unavailable: {provider}{(reason is null ? string.Empty : $" — {reason}")}");
