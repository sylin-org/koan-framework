using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.MySql.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Koan.Data.Connector.MySql.Discovery;

internal sealed class MySqlDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<MySqlDiscoveryAdapter> logger) : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Service;
    public override string[] Aliases => [];
    protected override Type GetFactoryType() => typeof(MySqlAdapterFactory);

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Constants.Configuration.ConnectionString] ??
        _configuration.GetConnectionString("MySql") ??
        _configuration.GetConnectionString(Constants.DefaultSource);

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        try
        {
            var builder = MySqlConnectionStrings.Normalize(baseUrl);
            builder.Database = Value(parameters, "database") ??
                               _configuration[Constants.Configuration.Database] ??
                               EmptyAs(builder.Database, Constants.DefaultDatabase);
            builder.UserID = Value(parameters, "userId") ??
                             _configuration[Constants.Configuration.UserId] ??
                             EmptyAs(builder.UserID, "root");
            builder.Password = Value(parameters, "password") ??
                               _configuration[Constants.Configuration.Password] ??
                               EmptyAs(builder.Password, "mysql");
            return builder.ConnectionString;
        }
        catch (Exception error)
        {
            ReportNormalizationFailure(baseUrl, error);
            return baseUrl;
        }
    }

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new MySqlConnection(serviceUrl);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand("SELECT 1", connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
        }
        catch (MySqlException error) when (error.Number == 1049)
        {
            // The server answered and the credentials work; the Koan database does not exist
            // yet. Managed lifecycle creates it before the first schema DDL, so this is
            // healthy - a fresh zero-configuration server must be discoverable, not refused.
            return true;
        }
    }

    private static string? Value(IDictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? Convert.ToString(value) : null;
    private static string EmptyAs(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
