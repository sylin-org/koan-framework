using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.SqlServer.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.SqlServer.Discovery;

internal sealed class SqlServerDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<SqlServerDiscoveryAdapter> logger) : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Service;
    public override string[] Aliases => [Constants.Provider, "microsoft.sqlserver"];
    protected override Type GetFactoryType() => typeof(SqlServerAdapterFactory);

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Constants.Configuration.ConnectionString] ??
        _configuration.GetConnectionString("SqlServer") ??
        _configuration.GetConnectionString("MSSQL") ??
        _configuration.GetConnectionString(Constants.DefaultSource);

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        try
        {
            var builder = Build(baseUrl);
            builder.InitialCatalog = Value(parameters, "database") ??
                                     _configuration[Constants.Configuration.Database] ??
                                     EmptyAs(builder.InitialCatalog, "Koan");
            builder.UserID = Value(parameters, "userId") ??
                             _configuration[Constants.Configuration.UserId] ??
                             EmptyAs(builder.UserID, "sa");
            builder.Password = Value(parameters, "password") ??
                               _configuration[Constants.Configuration.Password] ??
                               EmptyAs(builder.Password, "Your_password123");
            builder.TrustServerCertificate = true;
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
            await using var connection = new SqlConnection(serviceUrl);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand("SELECT 1", connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
        }
        catch (SqlException error) when (error.Number == 4060 || (error.Number == 18456 && error.State == 38))
        {
            // The server answered and the credentials work; the Koan database does not exist
            // yet. Managed lifecycle creates it before the first schema DDL, so this is
            // healthy - a fresh zero-configuration server must be discoverable, not refused.
            return true;
        }
    }

    private static SqlConnectionStringBuilder Build(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "mssql")
            return new SqlConnectionStringBuilder(value);
        var builder = new SqlConnectionStringBuilder
        {
            // Numeric loopback, never "localhost": SqlClient's pre-login handshake stalls for the
            // dual-stack resolution delay over ::1 (observed 15s connect timeouts), while 127.0.0.1
            // answers immediately.
            DataSource = $"{(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : uri.Host)},{(uri.Port > 0 ? uri.Port : 1433)}"
        };
        var user = uri.UserInfo.Split(':', 2);
        if (user.Length > 0 && user[0].Length > 0) builder.UserID = Uri.UnescapeDataString(user[0]);
        if (user.Length > 1) builder.Password = Uri.UnescapeDataString(user[1]);
        if (uri.AbsolutePath.Length > 1) builder.InitialCatalog = Uri.UnescapeDataString(uri.AbsolutePath[1..]);
        return builder;
    }

    private static string? Value(IDictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? Convert.ToString(value) : null;
    private static string EmptyAs(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
