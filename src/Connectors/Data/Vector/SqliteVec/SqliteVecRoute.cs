using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>Owns sqlite-vec placement without opening a database or loading the native extension.</summary>
internal static class SqliteVecRoute
{
    internal static SqliteVecRouteDecision Resolve(
        IConfiguration configuration,
        DataSourceRegistry sources,
        SqliteVecOptions options,
        IAdapterFactory sourceOwner,
        string source)
    {
        var ownDefault = ResolveOwnDefault(configuration, options);
        var own = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration,
            sources,
            Infrastructure.Constants.Provider.Name,
            source,
            ownDefault.ConnectionString,
            sourceOwner);

        if (!IsAutomatic(own))
            return new SqliteVecRouteDecision(own, source, ownDefault.Origin);

        var pairedDefault = ResolvePairedDefault(configuration);
        var paired = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration,
            sources,
            Infrastructure.Constants.Provider.PairedDataProvider,
            source,
            pairedDefault.ConnectionString,
            sourceOwner);

        return new SqliteVecRouteDecision(paired, source, pairedDefault.Origin);
    }

    internal static SqliteVecRouteDecision ResolveDefault(IConfiguration configuration, SqliteVecOptions? options = null)
    {
        var own = ResolveOwnDefault(configuration, options ?? new SqliteVecOptions());
        return IsAutomatic(own.ConnectionString) ? ResolvePairedDefault(configuration) : own;
    }

    internal static void PrepareFileSystem(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.Mode == SqliteOpenMode.Memory || string.IsNullOrWhiteSpace(builder.DataSource) ||
            string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            return;

        var fullPath = Path.GetFullPath(builder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static SqliteVecRouteDecision ResolveOwnDefault(IConfiguration configuration, SqliteVecOptions options)
    {
        var candidates = new (string Key, string? Value)[]
        {
            (Infrastructure.Constants.Configuration.DefaultSourceConnectionString,
                configuration[Infrastructure.Constants.Configuration.DefaultSourceConnectionString]),
            (Infrastructure.Constants.Configuration.ConnectionString,
                configuration[Infrastructure.Constants.Configuration.ConnectionString]),
            (Infrastructure.Constants.Configuration.ConnectionStringsSqliteVec,
                configuration.GetConnectionString("SqliteVec")),
            (SqliteVecOptions.Section, options.ConnectionString)
        };

        foreach (var (key, value) in candidates)
            if (!string.IsNullOrWhiteSpace(value) && !IsAutomatic(value))
                return new SqliteVecRouteDecision(value, "Default", key);

        return new SqliteVecRouteDecision(Infrastructure.Constants.Configuration.Automatic, "Default", "automatic pairing");
    }

    private static SqliteVecRouteDecision ResolvePairedDefault(IConfiguration configuration)
    {
        var candidates = new (string Key, string? Value)[]
        {
            (Infrastructure.Constants.Configuration.PairedDefaultSourceConnectionString,
                configuration[Infrastructure.Constants.Configuration.PairedDefaultSourceConnectionString]),
            (Infrastructure.Constants.Configuration.PairedConnectionString,
                configuration[Infrastructure.Constants.Configuration.PairedConnectionString]),
            (Infrastructure.Constants.Configuration.ConnectionStringsSqlite,
                configuration.GetConnectionString("Sqlite"))
        };

        foreach (var (key, value) in candidates)
            if (!string.IsNullOrWhiteSpace(value) && !IsAutomatic(value))
                return new SqliteVecRouteDecision(value, "Default", $"paired from {key}");

        return new SqliteVecRouteDecision(
            Infrastructure.Constants.Configuration.LocalFallback,
            "Default",
            "local SQLite fallback");
    }

    private static bool IsAutomatic(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value.Trim(), Infrastructure.Constants.Configuration.Automatic, StringComparison.OrdinalIgnoreCase);
}

internal sealed record SqliteVecRouteDecision(string ConnectionString, string Source, string Origin);
