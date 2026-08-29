using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.CouchDb.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.CouchDb.Discovery;

internal sealed class CouchDbDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<CouchDbDiscoveryAdapter> logger) : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Service;
    public override string[] Aliases => [];
    protected override Type GetFactoryType() => typeof(CouchDbAdapterFactory);

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Constants.Configuration.ConnectionString] ??
        _configuration.GetConnectionString("CouchDb") ??
        _configuration.GetConnectionString(Constants.DefaultSource);

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        var user = Value(parameters, "userId") ?? _configuration[Constants.Configuration.UserId];
        var password = Value(parameters, "password") ?? _configuration[Constants.Configuration.Password];
        if (string.IsNullOrWhiteSpace(user)) return baseUrl;
        try
        {
            var builder = new UriBuilder(baseUrl) { UserName = Uri.EscapeDataString(user) };
            if (!string.IsNullOrWhiteSpace(password)) builder.Password = Uri.EscapeDataString(password);
            return builder.Uri.ToString().TrimEnd('/');
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
        using var client = new Runtime.CouchDbClient(serviceUrl, userId: null, password: null);
        return await client.PingAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? Value(IDictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? Convert.ToString(value) : null;
}
