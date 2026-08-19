using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Koan.Data.Vector.Connector.PgVector;

internal sealed record PgVectorRoute(
    string Source,
    string ConnectionString,
    string Origin,
    DataSourcePlan Policy)
{
    internal static PgVectorRoute Resolve(
        IConfiguration configuration,
        DataSourceRegistry sources,
        PgVectorOptions options,
        IAdapterFactory owner,
        string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        string connection;
        string origin;
        if (IsPairedSource(sources.GetSource(name)?.Adapter))
        {
            connection = ResolvePaired(configuration, sources, owner, name, options.ConnectionString);
            origin = "paired PostgreSQL";
        }
        else
        {
            connection = AdapterConnectionResolver.ResolveRoutedConnection(
                configuration,
                sources,
                Infrastructure.Constants.Provider.Name,
                name,
                options.ConnectionString,
                owner);
            origin = "PgVector";
            if (IsAutomatic(connection))
            {
                connection = ResolvePaired(configuration, sources, owner, name, options.ConnectionString);
                origin = "paired PostgreSQL";
            }
        }

        if (IsAutomatic(connection))
            throw new InvalidOperationException(
                $"PgVector source '{name}' has no concrete PostgreSQL placement. Configure its ConnectionString or a paired PostgreSQL source.");
        if (string.Equals(origin, "paired PostgreSQL", StringComparison.Ordinal))
            connection = EnrichPaired(connection, configuration);
        var normalized = NormalizeConnectionString(connection);
        return new PgVectorRoute(
            name,
            normalized,
            origin,
            sources.GetPlan(name, Infrastructure.Constants.Provider.Name, normalized));
    }

    internal static (string ConnectionString, string Origin) ResolveDefault(IConfiguration configuration)
    {
        var own = FirstConcrete(
            configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString],
            configuration.GetConnectionString("PgVector"));
        if (!IsAutomatic(own)) return (NormalizeConnectionString(own!), "PgVector");
        var paired = FirstConcrete(
            configuration[Infrastructure.Constants.Configuration.PairedConnectionString],
            configuration.GetConnectionString("Postgres"));
        return IsAutomatic(paired)
            ? (Infrastructure.Constants.Configuration.Automatic, "automatic discovery")
            : (NormalizeConnectionString(paired!), "paired PostgreSQL");
    }

    internal static string NormalizeConnectionString(string value)
    {
        try
        {
            var builder = Build(value);
            if (string.IsNullOrWhiteSpace(builder.Host))
                throw new InvalidOperationException("a PostgreSQL host is required");
            return builder.ConnectionString;
        }
        catch (Exception error) when (error is ArgumentException or UriFormatException or
                                            KeyNotFoundException or FormatException or InvalidCastException)
        {
            throw new InvalidOperationException(
                "PgVector requires a PostgreSQL connection string or postgres:// URI.", error);
        }
    }

    internal static NpgsqlConnectionStringBuilder Build(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("postgres" or "postgresql"))
            return new NpgsqlConnectionStringBuilder(value);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : Infrastructure.Constants.Defaults.Port
        };
        var user = uri.UserInfo.Split(':', 2);
        if (user.Length > 0 && user[0].Length > 0) builder.Username = Uri.UnescapeDataString(user[0]);
        if (user.Length > 1) builder.Password = Uri.UnescapeDataString(user[1]);
        if (uri.AbsolutePath.Length > 1) builder.Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]);
        if (!string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("PostgreSQL connection URIs cannot contain a fragment.", nameof(value));
        foreach (var pair in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Decode(parts[0]).Replace('_', ' ');
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("PostgreSQL connection URI query keys cannot be blank.", nameof(value));
            builder[key] = parts.Length == 1 ? string.Empty : Decode(parts[1]);
        }
        return builder;
    }

    private static string Decode(string value) =>
        Uri.UnescapeDataString(value.Replace('+', ' '));

    private static string? FirstConcrete(params string?[] values) =>
        values.FirstOrDefault(static value => !IsAutomatic(value));

    private static string ResolvePaired(
        IConfiguration configuration,
        DataSourceRegistry sources,
        IAdapterFactory owner,
        string source,
        string? resolvedDefault)
    {
        var pairedDefault = FirstConcrete(
            configuration[Infrastructure.Constants.Configuration.PairedConnectionString],
            configuration.GetConnectionString("Postgres"),
            resolvedDefault);
        return AdapterConnectionResolver.ResolveRoutedConnection(
            configuration,
            sources,
            Infrastructure.Constants.Provider.PairedDataProvider,
            source,
            pairedDefault,
            owner);
    }

    private static bool IsPairedSource(string? adapter) =>
        adapter is not null &&
        (adapter.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
         adapter.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
         adapter.Equals("npgsql", StringComparison.OrdinalIgnoreCase));

    private static string EnrichPaired(string connection, IConfiguration configuration)
    {
        var builder = Build(connection);
        if (string.IsNullOrWhiteSpace(builder.Database))
            builder.Database = FirstConcrete(
                configuration[Infrastructure.Constants.Configuration.PairedDatabase],
                "Koan")!;
        if (string.IsNullOrWhiteSpace(builder.Username))
            builder.Username = FirstConcrete(
                configuration[Infrastructure.Constants.Configuration.PairedUsername],
                "postgres")!;
        if (string.IsNullOrWhiteSpace(builder.Password))
            builder.Password = FirstConcrete(
                configuration[Infrastructure.Constants.Configuration.PairedPassword],
                "postgres")!;
        if (string.IsNullOrWhiteSpace(builder.SearchPath))
            builder.SearchPath = FirstConcrete(
                configuration[Infrastructure.Constants.Configuration.PairedSearchPath]);
        return builder.ConnectionString;
    }

    private static bool IsAutomatic(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), Infrastructure.Constants.Configuration.Automatic, StringComparison.OrdinalIgnoreCase);
}
