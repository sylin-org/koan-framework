using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Postgres.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Discovery;

internal sealed class PostgresDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<PostgresDiscoveryAdapter> logger) : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Provider;
    public override string[] Aliases => ["postgresql", "npgsql"];

    protected override Type GetFactoryType() => typeof(PostgresAdapterFactory);

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Constants.Configuration.ConnectionString] ??
        _configuration.GetConnectionString("Postgres") ??
        _configuration.GetConnectionString(Constants.DefaultSource);

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        try
        {
            var builder = Build(baseUrl);
            builder.Database = Value(parameters, "database") ??
                               _configuration[Constants.Configuration.Database] ??
                               EmptyAs(builder.Database, "Koan");
            builder.Username = Value(parameters, "username") ??
                               _configuration[Constants.Configuration.Username] ??
                               EmptyAs(builder.Username, "postgres");
            builder.Password = Value(parameters, "password") ??
                               _configuration[Constants.Configuration.Password] ??
                               EmptyAs(builder.Password, "postgres");
            return builder.ConnectionString;
        }
        catch (Exception error)
        {
            ReportNormalizationFailure(baseUrl, error);
            return baseUrl;
        }
    }

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(serviceUrl);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static NpgsqlConnectionStringBuilder Build(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("postgres" or "postgresql"))
            return new NpgsqlConnectionStringBuilder(value);

        var builder = new NpgsqlConnectionStringBuilder { Host = uri.Host, Port = uri.Port > 0 ? uri.Port : 5432 };
        var user = uri.UserInfo.Split(':', 2);
        if (user.Length > 0 && user[0].Length > 0) builder.Username = Uri.UnescapeDataString(user[0]);
        if (user.Length > 1) builder.Password = Uri.UnescapeDataString(user[1]);
        if (uri.AbsolutePath.Length > 1) builder.Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]);
        return builder;
    }

    private static string? Value(IDictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

    private static string EmptyAs(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
