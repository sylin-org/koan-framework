namespace Koan.Secrets.Abstractions;

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