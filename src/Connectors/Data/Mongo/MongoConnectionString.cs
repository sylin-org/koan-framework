using System;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// The single owner of MongoDB connection-string construction (ARCH-0103 §L). Folds the four formerly
/// scattered copies — <c>MongoOptionsConfigurator.MergeMongoOverrides</c> + its <c>BuildMongoConnectionString</c>,
/// <c>MongoDiscoveryAdapter.ApplyConnectionParameters</c>, and <c>MongoOrchestrationEvaluator</c>'s
/// <c>BuildMongoConnectionString</c> + <c>ExtractHostFromConnectionString</c> — into one cohesive helper.
/// <para>
/// All routines use string manipulation rather than <see cref="System.Uri"/> so that replica-set
/// connection strings with comma-separated hosts (which <c>Uri.TryCreate</c> rejects as invalid
/// RFC 3986) and the <c>mongodb+srv</c> scheme are preserved verbatim.
/// </para>
/// <para>
/// Two distinct override policies are kept as separate, explicitly-named methods because they have
/// genuinely different semantics by design: <see cref="MergeOverrides"/> (used when resolving a
/// user/ZenGarden-supplied connection string) PRESERVES any existing auth and only fills the database
/// when the path is empty; <see cref="ApplyParameters"/> (used by discovery on a freshly-built base URL)
/// REPLACES auth and the database. Forcing them into one method would change behavior on one of the two
/// call sites.
/// </para>
/// </summary>
internal static class MongoConnectionString
{
    /// <summary>
    /// Builds a single-host <c>mongodb://</c> connection string from components.
    /// </summary>
    public static string Build(string hostname, int port, string? database, string? username, string? password)
    {
        var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password ?? ""}@";
        var db = string.IsNullOrEmpty(database) ? "" : $"/{database}";
        return $"mongodb://{auth}{hostname}:{port}{db}";
    }

    /// <summary>
    /// Builds a single-host <c>mongodb://</c> connection string from a <c>host[:port]</c> endpoint string
    /// (orchestration variant — defaults the port to 27017 when absent).
    /// </summary>
    public static string Build(string hostPort, string? database, string? username, string? password)
    {
        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1] : "27017";

        var auth = string.IsNullOrEmpty(username) ? "" : $"{username}:{password ?? ""}@";
        var db = string.IsNullOrEmpty(database) ? "" : $"/{database}";
        return $"mongodb://{auth}{host}:{port}{db}";
    }

    /// <summary>
    /// PRESERVE-policy merge: applies database, username, and password overrides to an existing MongoDB
    /// connection string, keeping any auth already present and only filling the database when the path is
    /// empty. Handles both single-host and replica-set (multi-host) connection strings without routing
    /// through <see cref="System.Uri"/>/<c>UriBuilder</c>.
    /// </summary>
    public static string MergeOverrides(
        string connectionString,
        string? databaseName,
        string? username,
        string? password)
    {
        // Format: mongodb[+srv]://[user:pass@]hosts[/database][?options]
        var schemeEnd = connectionString.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return connectionString;

        var scheme = connectionString[..(schemeEnd + 3)]; // e.g. "mongodb://"
        var rest = connectionString[(schemeEnd + 3)..];    // everything after "://"

        // Split existing auth from host portion
        string existingAuth = "";
        var atIndex = rest.IndexOf('@');
        var slashIndex = rest.IndexOf('/');
        var questionIndex = rest.IndexOf('?');

        // '@' must appear before any '/' or '?' to be auth (not part of query params)
        if (atIndex >= 0 && (slashIndex < 0 || atIndex < slashIndex) && (questionIndex < 0 || atIndex < questionIndex))
        {
            existingAuth = rest[..atIndex];
            rest = rest[(atIndex + 1)..];
        }

        // Split hosts from path+query
        string hosts;
        string pathAndQuery;
        var pathStart = rest.IndexOf('/');
        if (pathStart >= 0)
        {
            hosts = rest[..pathStart];
            pathAndQuery = rest[pathStart..]; // includes leading '/'
        }
        else
        {
            var queryStart = rest.IndexOf('?');
            if (queryStart >= 0)
            {
                hosts = rest[..queryStart];
                pathAndQuery = rest[queryStart..];
            }
            else
            {
                hosts = rest;
                pathAndQuery = "";
            }
        }

        // Apply auth override (only if not already present)
        var auth = !string.IsNullOrWhiteSpace(existingAuth)
            ? existingAuth + "@"
            : !string.IsNullOrWhiteSpace(username)
                ? $"{username}:{password ?? ""}@"
                : "";

        // Apply database override (only if not already present in path)
        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            // Extract existing path portion (before any '?')
            var existingPath = pathAndQuery;
            var existingQuery = "";
            var qIdx = pathAndQuery.IndexOf('?');
            if (qIdx >= 0)
            {
                existingPath = pathAndQuery[..qIdx];
                existingQuery = pathAndQuery[qIdx..];
            }

            if (string.IsNullOrWhiteSpace(existingPath.Trim('/')))
            {
                pathAndQuery = "/" + databaseName.Trim() + existingQuery;
            }
        }

        return (scheme + auth + hosts + pathAndQuery).TrimEnd('/');
    }

    /// <summary>
    /// REPLACE-policy merge: applies discovery-supplied database/username/password to a freshly-built base
    /// URL, replacing any existing auth and database. Auth is applied only when BOTH a username and a
    /// password are supplied (mirroring the former dictionary-presence check). Uses string manipulation
    /// rather than <see cref="System.Uri"/> to support comma-separated replica-set hosts. The caller is
    /// responsible for the fail-safe (the discovery override wraps this in try/catch so a malformed base
    /// URL falls back to the original — preserving its debug log).
    /// </summary>
    public static string ApplyParameters(string baseUrl, string? database, string? username, string? password)
    {
        // Format: mongodb[+srv]://[existing-auth@]hosts[/db][?options]
        var schemeEnd = baseUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return baseUrl;

        var scheme = baseUrl[..(schemeEnd + 3)];
        var rest = baseUrl[(schemeEnd + 3)..];

        // Detect existing auth (@ must appear before any / or ?)
        var atIndex = rest.IndexOf('@');
        var slashIndex = rest.IndexOf('/');
        if (atIndex >= 0 && (slashIndex < 0 || atIndex < slashIndex))
        {
            rest = rest[(atIndex + 1)..];
        }

        // Split hosts from path+query
        string hosts;
        string trailing = "";
        var pathStart = rest.IndexOf('/');
        if (pathStart >= 0)
        {
            hosts = rest[..pathStart];
            trailing = rest[pathStart..];
        }
        else
        {
            var queryStart = rest.IndexOf('?');
            hosts = queryStart >= 0 ? rest[..queryStart] : rest;
            trailing = queryStart >= 0 ? rest[queryStart..] : "";
        }

        var auth = "";
        if (username is not null && password is not null)
        {
            auth = $"{username}:{password}@";
        }

        if (database is not null)
        {
            // Replace existing path with requested database, preserve query
            var qIdx = trailing.IndexOf('?');
            var query = qIdx >= 0 ? trailing[qIdx..] : "";
            trailing = $"/{database}{query}";
        }

        return $"{scheme}{auth}{hosts}{trailing}";
    }

    /// <summary>
    /// Resolves the effective connection string for a routed (non-Default) source. A blank or
    /// <c>"auto"</c> resolved value means the source carries no usable connection of its own and relies on
    /// runtime discovery — but <see cref="AdapterConnectionResolver"/> works from raw configuration and
    /// cannot return the discovery-resolved string, so it yields the literal <c>"auto"</c>. In that case
    /// fall back to <paramref name="resolvedDefault"/> (the Default options' already-discovery-resolved
    /// connection) rather than keying a per-source client pool on an unusable string. Otherwise the
    /// source's own connection string wins. (ARCH-0103 catalogue §8 P4 — the non-Default "auto" quirk;
    /// this revives the factory's intended-but-dead <c>baseOptions.ConnectionString</c> fallback.)
    /// </summary>
    public static string ResolveRoutedConnection(string? sourceConnection, string resolvedDefault)
        => string.IsNullOrWhiteSpace(sourceConnection)
           || string.Equals(sourceConnection.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
            ? resolvedDefault
            : sourceConnection;

    /// <summary>
    /// Extracts the <c>host:port</c> endpoint from a MongoDB connection string (or returns a plain
    /// <c>host:port</c> input unchanged). Returns <c>null</c> when parsing fails.
    /// </summary>
    public static string? ExtractHost(string connectionString)
    {
        try
        {
            // Handle both mongodb:// and plain host:port formats
            if (connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(connectionString);
                return $"{uri.Host}:{uri.Port}";
            }

            // Assume it's just host:port
            return connectionString;
        }
        catch
        {
            return null;
        }
    }
}
