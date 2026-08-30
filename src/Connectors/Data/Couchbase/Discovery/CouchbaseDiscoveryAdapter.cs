using Couchbase;
using Couchbase.Core.IO.Authentication.Authenticators;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Couchbase.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.Couchbase.Discovery;

internal sealed class CouchbaseDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<CouchbaseDiscoveryAdapter> logger) : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Constants.Discovery.ServiceName;
    public override string[] Aliases => [Constants.Alias];

    protected override Type GetFactoryType() => typeof(CouchbaseAdapterFactory);

    protected override string? ReadExplicitConfiguration() =>
        _configuration[Constants.Configuration.ConnectionString] ??
        _configuration.GetConnectionString("Couchbase") ??
        _configuration.GetConnectionString(Constants.DefaultSource);

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var values = Environment.GetEnvironmentVariable(Constants.Discovery.CouchbaseUrls) ??
                     Environment.GetEnvironmentVariable(Constants.Discovery.CouchbaseAliasUrls);
        return string.IsNullOrWhiteSpace(values)
            ? []
            : values.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static value => new DiscoveryCandidate(
                    Normalize(value),
                    "environment-couchbase-urls",
                    DiscoveryCandidatePriority.Environment));
    }

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters) =>
        Normalize(baseUrl);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var username = Value(context.Parameters, "username") ?? "Administrator";
        var password = Value(context.Parameters, "password") ?? "password";
        var options = new ClusterOptions { ConnectionString = Normalize(serviceUrl) };
        options.WithAuthenticator(new PasswordAuthenticator(username, password));
        using var cluster = await Cluster.ConnectAsync(options).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _ = await cluster.PingAsync().ConfigureAwait(false);
        return true;
    }

    private static string Normalize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("couchbase://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("couchbases://", StringComparison.OrdinalIgnoreCase))
            return trimmed.EndsWith(":8091", StringComparison.Ordinal) ? trimmed[..^":8091".Length] : trimmed;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "couchbase://" + StripConsolePort(trimmed[7..]);
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "couchbases://" + StripConsolePort(trimmed[8..]);
        return "couchbase://" + trimmed;

        // 8091 is the web-console port, not an SDK bootstrap port: a port-qualified
        // couchbase://host:8091 connection string never receives a config stream (0 nodes).
        // The conventional candidate is the console URL, so the console port is dropped and the
        // SDK falls back to its own bootstrap ports.
        static string StripConsolePort(string hostPort) =>
            hostPort.EndsWith(":8091", StringComparison.Ordinal) ? hostPort[..^":8091".Length] : hostPort;
    }

    private static string? Value(IDictionary<string, object>? values, string key) =>
        values?.TryGetValue(key, out var value) == true ? Convert.ToString(value) : null;
}
