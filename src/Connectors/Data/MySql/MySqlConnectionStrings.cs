using MySqlConnector;

namespace Koan.Data.Connector.MySql;

internal static class MySqlConnectionStrings
{
    public static MySqlConnectionStringBuilder Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "mysql", StringComparison.OrdinalIgnoreCase))
        {
            try { return new MySqlConnectionStringBuilder(value); }
            catch (ArgumentException error)
            {
                throw new ArgumentException(
                    "The MySQL connection string is neither a valid MySqlConnector key-value string nor a mysql:// URI.",
                    nameof(value), error);
            }
        }
        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("A MySQL connection URI requires a host.", nameof(value));

        var builder = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = checked((uint)(uri.Port > 0 ? uri.Port : 3306))
        };
        var user = uri.UserInfo.Split(':', 2);
        if (user.Length > 0 && user[0].Length > 0) builder.UserID = Uri.UnescapeDataString(user[0]);
        if (user.Length > 1) builder.Password = Uri.UnescapeDataString(user[1]);
        if (uri.AbsolutePath.Length > 1) builder.Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]);

        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            var option = pair.Length == 1 ? "true" : Uri.UnescapeDataString(pair[1]);
            try { builder[key] = option; }
            catch (ArgumentException error)
            {
                throw new ArgumentException(
                    $"MySQL connection URI option '{key}' is not recognized. Use a MySqlConnector connection-string keyword.",
                    nameof(value), error);
            }
        }
        return builder;
    }

}
