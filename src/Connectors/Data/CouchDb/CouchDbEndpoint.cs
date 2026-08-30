namespace Koan.Data.Connector.CouchDb;

/// <summary>
/// The one tolerant reader for CouchDB endpoint values. `couchdb://user:password@host:port`
/// normalizes to an http URL plus credentials; an http(s) URL carries any credentials in its
/// user-info the same way. Shared by the factory (route resolution), the client (wire base
/// address), and discovery (health validation) so all three accept exactly the same strings.
/// </summary>
internal static class CouchDbEndpoint
{
    public static (Uri HttpEndpoint, string? UserId, string? Password) Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme is "couchdb" or "http" or "https")
        {
            var scheme = uri.Scheme == "couchdb" ? "http" : uri.Scheme;
            var builder = new UriBuilder(uri) { Scheme = scheme, UserName = string.Empty, Password = string.Empty };
            if (uri.Scheme == "couchdb" && uri.Port < 0) builder.Port = 5984;
            var user = string.IsNullOrEmpty(uri.UserInfo) ? null : Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[0]);
            var password = string.IsNullOrEmpty(uri.UserInfo)
                ? null
                : (uri.UserInfo.Split(':', 2) is { Length: 2 } parts ? Uri.UnescapeDataString(parts[1]) : null);
            return (builder.Uri, user, password);
        }
        throw new ArgumentException(
            $"The CouchDB endpoint '{value}' is neither a couchdb:// URI nor an http(s) URL.", nameof(value));
    }
}
