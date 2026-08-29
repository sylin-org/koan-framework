using FirebirdSql.Data.FirebirdClient;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Firebird.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.Firebird.Discovery;

internal sealed class FirebirdDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<FirebirdDiscoveryAdapter> logger) : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Service;
    public override string[] Aliases => [];
    protected override Type GetFactoryType() => typeof(FirebirdAdapterFactory);

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Constants.Configuration.ConnectionString] ??
        _configuration.GetConnectionString("Firebird") ??
        _configuration.GetConnectionString(Constants.DefaultSource);

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        try
        {
            var builder = FirebirdConnectionStrings.Normalize(baseUrl);
            builder.Database = Value(parameters, "database") ??
                               _configuration[Constants.Configuration.Database] ??
                               EmptyAs(builder.Database, Constants.DefaultDatabase);
            builder.UserID = Value(parameters, "userId") ??
                             _configuration[Constants.Configuration.UserId] ??
                             EmptyAs(builder.UserID, "SYSDBA");
            builder.Password = Value(parameters, "password") ??
                               _configuration[Constants.Configuration.Password] ??
                               EmptyAs(builder.Password, "masterkey");
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
        await using var connection = new FbConnection(serviceUrl);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM RDB$DATABASE";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static string? Value(IDictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? Convert.ToString(value) : null;
    private static string EmptyAs(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
