using System;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Core.Orchestration;

/// <summary>
/// Static utility for parsing and building connection strings across multiple database providers.
/// Provides a unified interface for extracting connection components and reconstructing connection strings.
/// </summary>
/// <remarks>
/// ARCH-0068: Static helper pattern for pure parsing functions with no state or dependencies.
/// Used across discovery adapters, compose generation, and provenance reporting.
/// </remarks>
public static class ConnectionStringParser
{
    /// <summary>
    /// Parses a connection string into structured components based on the provider type.
    /// </summary>
    /// <param name="connectionString">The connection string to parse</param>
    /// <param name="providerType">The database provider type (postgres, sqlserver, mongodb, redis, sqlite)</param>
    /// <returns>Structured connection string components</returns>
    public static ConnectionStringComponents Parse(string connectionString, string providerType)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return ConnectionStringComponents.Empty;

        return providerType.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => ParsePostgres(connectionString),
            "sqlserver" or "mssql" => ParseSqlServer(connectionString),
            "sqlite" => ParseSqlite(connectionString),
            "redis" => ParseRedis(connectionString),
            "mongodb" or "mongo" => ParseMongo(connectionString),
            _ => ParseGeneric(connectionString)
        };
    }

    /// <summary>
    /// Builds a connection string from structured components for the specified provider.
    /// </summary>
    /// <param name="components">The connection string components</param>
    /// <param name="providerType">The database provider type</param>
    /// <returns>Formatted connection string</returns>
    public static string Build(ConnectionStringComponents components, string providerType)
    {
        return providerType.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => BuildPostgres(components),
            "sqlserver" or "mssql" => BuildSqlServer(components),
            "sqlite" => BuildSqlite(components),
            "redis" => BuildRedis(components),
            "mongodb" or "mongo" => BuildMongo(components),
            _ => BuildGeneric(components)
        };
    }

    /// <summary>
    /// Extracts just the host and port from a connection string.
    /// Useful for service discovery and compose generation.
    /// </summary>
    public static (string Host, int Port) ExtractEndpoint(string connectionString, string providerType)
    {
        var components = Parse(connectionString, providerType);
        return (components.Host, components.Port);
    }

    #region PostgreSQL

    private static ConnectionStringComponents ParsePostgres(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string host = "localhost";
        int port = 5432;
        string? database = null;
        string? username = null;
        string? password = null;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            var key = kv[0].Trim();
            var value = kv[1].Trim();

            switch (key.ToLowerInvariant())
            {
                case "host":
                case "server":
                    host = value;
                    break;
                case "port":
                    port = int.TryParse(value, out var p) ? p : 5432;
                    break;
                case "database":
                    database = value;
                    break;
                case "username":
                case "user id":
                case "userid":
                case "uid":
                    username = value;
                    break;
                case "password":
                case "pwd":
                    password = value;
                    break;
                default:
                    parameters[key] = value;
                    break;
            }
        }

        return new ConnectionStringComponents(host, port, database, username, password, parameters);
    }

    private static string BuildPostgres(ConnectionStringComponents components)
    {
        var parts = new List<string>
        {
            $"Host={components.Host}",
            $"Port={components.Port}"
        };

        if (!string.IsNullOrWhiteSpace(components.Database))
            parts.Add($"Database={components.Database}");

        if (!string.IsNullOrWhiteSpace(components.Username))
            parts.Add($"Username={components.Username}");

        if (!string.IsNullOrWhiteSpace(components.Password))
            parts.Add($"Password={components.Password}");

        foreach (var param in components.Parameters)
            parts.Add($"{param.Key}={param.Value}");

        return string.Join(";", parts);
    }

    #endregion

    #region SQL Server

    private static ConnectionStringComponents ParseSqlServer(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string host = "localhost";
        int port = 1433;
        string? database = null;
        string? username = null;
        string? password = null;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            var key = kv[0].Trim();
            var value = kv[1].Trim();

            switch (key.ToLowerInvariant())
            {
                case "server":
                case "data source":
                    // SQL Server format: "server,port" or "server"
                    var serverParts = value.Split(',');
                    host = serverParts[0];
                    if (serverParts.Length > 1 && int.TryParse(serverParts[1], out var p))
                        port = p;
                    break;
                case "database":
                case "initial catalog":
                    database = value;
                    break;
                case "user id":
                case "uid":
                    username = value;
                    break;
                case "password":
                case "pwd":
                    password = value;
                    break;
                default:
                    parameters[key] = value;
                    break;
            }
        }

        return new ConnectionStringComponents(host, port, database, username, password, parameters);
    }

    private static string BuildSqlServer(ConnectionStringComponents components)
    {
        var parts = new List<string>
        {
            components.Port != 1433
                ? $"Server={components.Host},{components.Port}"
                : $"Server={components.Host}"
        };

        if (!string.IsNullOrWhiteSpace(components.Database))
            parts.Add($"Database={components.Database}");

        if (!string.IsNullOrWhiteSpace(components.Username))
            parts.Add($"User Id={components.Username}");

        if (!string.IsNullOrWhiteSpace(components.Password))
            parts.Add($"Password={components.Password}");

        foreach (var param in components.Parameters)
            parts.Add($"{param.Key}={param.Value}");

        return string.Join(";", parts);
    }

    #endregion

    #region MongoDB

    private static ConnectionStringComponents ParseMongo(string connectionString)
    {
        // MongoDB format: mongodb://[username:password@]host[:port][/database][?options]
        if (!connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase))
        {
            // Fallback to generic parsing
            return ParseGeneric(connectionString);
        }

        var uri = new Uri(connectionString);
        string host = uri.Host;
        int port = uri.Port > 0 ? uri.Port : 27017;
        string? database = string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/"
            ? null
            : uri.AbsolutePath.TrimStart('/');

        string? username = null;
        string? password = null;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var userParts = uri.UserInfo.Split(':', 2);
            username = userParts[0];
            if (userParts.Length > 1)
                password = userParts[1];
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(uri.Query))
        {
            foreach (var param in uri.Query.TrimStart('?').Split('&'))
            {
                var kv = param.Split('=', 2);
                if (kv.Length == 2)
                    parameters[kv[0]] = kv[1];
            }
        }

        return new ConnectionStringComponents(host, port, database, username, password, parameters);
    }

    private static string BuildMongo(ConnectionStringComponents components)
    {
        var auth = string.IsNullOrWhiteSpace(components.Username)
            ? string.Empty
            : string.IsNullOrWhiteSpace(components.Password)
                ? $"{components.Username}@"
                : $"{components.Username}:{components.Password}@";

        var database = string.IsNullOrWhiteSpace(components.Database)
            ? string.Empty
            : $"/{components.Database}";

        var query = components.Parameters.Count == 0
            ? string.Empty
            : "?" + string.Join("&", components.Parameters.Select(p => $"{p.Key}={p.Value}"));

        return $"mongodb://{auth}{components.Host}:{components.Port}{database}{query}";
    }

    #endregion

    #region Redis

    private static ConnectionStringComponents ParseRedis(string connectionString)
    {
        // Redis format: host:port[,option=value,...]
        // Or: host:port,password=xxx,ssl=true,...
        var parts = connectionString.Split(',');
        string host = "localhost";
        int port = 6379;
        string? password = null;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // First part is always host:port
        if (parts.Length > 0)
        {
            var endpoint = parts[0].Split(':', 2);
            host = endpoint[0];
            if (endpoint.Length > 1 && int.TryParse(endpoint[1], out var p))
                port = p;
        }

        // Remaining parts are options
        for (int i = 1; i < parts.Length; i++)
        {
            var kv = parts[i].Split('=', 2);
            if (kv.Length == 2)
            {
                var key = kv[0].Trim();
                var value = kv[1].Trim();

                if (key.Equals("password", StringComparison.OrdinalIgnoreCase))
                    password = value;
                else
                    parameters[key] = value;
            }
        }

        return new ConnectionStringComponents(host, port, null, null, password, parameters);
    }

    private static string BuildRedis(ConnectionStringComponents components)
    {
        var parts = new List<string> { $"{components.Host}:{components.Port}" };

        if (!string.IsNullOrWhiteSpace(components.Password))
            parts.Add($"password={components.Password}");

        foreach (var param in components.Parameters)
            parts.Add($"{param.Key}={param.Value}");

        return string.Join(",", parts);
    }

    #endregion

    #region SQLite

    private static ConnectionStringComponents ParseSqlite(string connectionString)
    {
        // SQLite format: Data Source=path;[options]
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string? database = null;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            var key = kv[0].Trim();
            var value = kv[1].Trim();

            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                database = value;
            else
                parameters[key] = value;
        }

        // SQLite is file-based, no host/port
        return new ConnectionStringComponents("localhost", 0, database, null, null, parameters);
    }

    private static string BuildSqlite(ConnectionStringComponents components)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(components.Database))
            parts.Add($"Data Source={components.Database}");

        foreach (var param in components.Parameters)
            parts.Add($"{param.Key}={param.Value}");

        return string.Join(";", parts);
    }

    #endregion

    #region Generic

    private static ConnectionStringComponents ParseGeneric(string connectionString)
    {
        // Generic key=value parsing
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string host = "localhost";
        int port = 0;
        string? database = null;
        string? username = null;
        string? password = null;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            var key = kv[0].Trim();
            var value = kv[1].Trim();

            switch (key.ToLowerInvariant())
            {
                case "host":
                case "server":
                    host = value;
                    break;
                case "port":
                    int.TryParse(value, out port);
                    break;
                case "database":
                    database = value;
                    break;
                case "username":
                case "user":
                    username = value;
                    break;
                case "password":
                    password = value;
                    break;
                default:
                    parameters[key] = value;
                    break;
            }
        }

        return new ConnectionStringComponents(host, port, database, username, password, parameters);
    }

    private static string BuildGeneric(ConnectionStringComponents components)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(components.Host))
            parts.Add($"Host={components.Host}");

        if (components.Port > 0)
            parts.Add($"Port={components.Port}");

        if (!string.IsNullOrWhiteSpace(components.Database))
            parts.Add($"Database={components.Database}");

        if (!string.IsNullOrWhiteSpace(components.Username))
            parts.Add($"Username={components.Username}");

        if (!string.IsNullOrWhiteSpace(components.Password))
            parts.Add($"Password={components.Password}");

        foreach (var param in components.Parameters)
            parts.Add($"{param.Key}={param.Value}");

        return string.Join(";", parts);
    }

    #endregion
}

/// <summary>
/// Structured representation of connection string components.
/// Immutable record for thread-safe parsing results.
/// </summary>
public record ConnectionStringComponents(
    string Host,
    int Port,
    string? Database,
    string? Username,
    string? Password,
    Dictionary<string, string> Parameters)
{
    /// <summary>
    /// Empty connection string components for null/empty connection strings.
    /// </summary>
    public static readonly ConnectionStringComponents Empty = new(
        "localhost",
        0,
        null,
        null,
        null,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
