using FirebirdSql.Data.FirebirdClient;

namespace Koan.Data.Connector.Firebird;

internal static class FirebirdConnectionStrings
{
    /// <summary>
    /// Normalizes a firebird:// URI or a FirebirdClient key-value string into builder form, so the
    /// adapter can adjust the Database (source-scoped placement) without re-parsing by hand.
    /// </summary>
    public static FbConnectionStringBuilder Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "firebird", StringComparison.OrdinalIgnoreCase))
        {
            try { return new FbConnectionStringBuilder(value); }
            catch (ArgumentException error)
            {
                throw new ArgumentException(
                    "The Firebird connection string is neither a valid FirebirdClient key-value string nor a firebird:// URI.",
                    nameof(value), error);
            }
        }
        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("A Firebird connection URI requires a host.", nameof(value));

        var builder = new FbConnectionStringBuilder
        {
            DataSource = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 3050,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.Length > 1 ? uri.AbsolutePath[1..] : string.Empty)
        };
        var user = uri.UserInfo.Split(':', 2);
        if (user.Length > 0 && user[0].Length > 0) builder.UserID = Uri.UnescapeDataString(user[0]);
        if (user.Length > 1) builder.Password = Uri.UnescapeDataString(user[1]);

        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            var option = pair.Length == 1 ? "true" : Uri.UnescapeDataString(pair[1]);
            try { builder[key] = option; }
            catch (ArgumentException error)
            {
                throw new ArgumentException(
                    $"Firebird connection URI option '{key}' is not recognized. Use a FirebirdClient connection-string keyword.",
                    nameof(value), error);
            }
        }
        return builder;
    }
}
