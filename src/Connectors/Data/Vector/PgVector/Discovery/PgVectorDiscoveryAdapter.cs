using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Koan.Data.Vector.Connector.PgVector.Discovery;

internal sealed class PgVectorDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<PgVectorDiscoveryAdapter> logger)
    : ServiceDiscoveryAdapterBase(configuration, logger)
{
    public override string ServiceName => Infrastructure.Constants.Provider.Name;
    public override string[] Aliases => Infrastructure.Constants.Provider.DiscoveryAliases.ToArray();

    protected override Type GetFactoryType() => typeof(PgVectorVectorAdapterFactory);

    protected override string? ReadExplicitConfiguration() => FirstConcrete(
        _configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString],
        _configuration.GetConnectionString("PgVector"),
        _configuration[Infrastructure.Constants.Configuration.PairedConnectionString],
        _configuration.GetConnectionString("Postgres"));

    protected override string ApplyConnectionParameters(string baseUrl, IDictionary<string, object> parameters)
    {
        try
        {
            var builder = PgVectorRoute.Build(baseUrl);
            builder.Database = Value(parameters, "database") ??
                               Concrete(builder.Database) ??
                               _configuration[Infrastructure.Constants.Configuration.PairedDatabase] ??
                               "Koan";
            builder.Username = Value(parameters, "username") ??
                               Concrete(builder.Username) ??
                               _configuration[Infrastructure.Constants.Configuration.PairedUsername] ??
                               "postgres";
            builder.Password = Value(parameters, "password") ??
                               Concrete(builder.Password) ??
                               _configuration[Infrastructure.Constants.Configuration.PairedPassword] ??
                               "postgres";
            var searchPath = Value(parameters, "searchPath") ??
                             Concrete(builder.SearchPath) ??
                             _configuration[Infrastructure.Constants.Configuration.PairedSearchPath];
            if (!string.IsNullOrWhiteSpace(searchPath)) builder.SearchPath = searchPath;
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
        try
        {
            await using var connection = new NpgsqlConnection(PgVectorRoute.NormalizeConnectionString(serviceUrl));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector')",
                connection);
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (PostgresException error) when (error.SqlState == "3D000")
        {
            // The server answered and the credentials work; the Koan database does not exist yet.
            // Managed lifecycle creates it before the first vector write, so this is healthy.
            return true;
        }
    }

    protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates()
    {
        var values = Environment.GetEnvironmentVariable("PGVECTOR_URLS");
        if (!string.IsNullOrWhiteSpace(values))
            return values.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static endpoint => Candidate(endpoint, "environment-pgvector-urls"));

        var value = Environment.GetEnvironmentVariable("PGVECTOR_URL");
        if (!string.IsNullOrWhiteSpace(value))
            return [Candidate(value, "environment-pgvector-url")];

        // Npgsql key/value connection strings use semicolons as syntax, not endpoint separators.
        value = Environment.GetEnvironmentVariable("PGVECTOR_CONNECTION_STRING");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : [Candidate(value, "environment-pgvector-connection")];
    }

    protected override string? ReadAspireServiceDiscovery() =>
        _configuration["services:pgvector:default:0"] ??
        _configuration["services:postgres:default:0"];

    protected override IEnumerable<DiscoveryCandidate> BuildRuntimeCandidates(KoanServiceAttribute attribute)
    {
        if (KoanEnv.InContainer)
            yield return Candidate(
                $"postgres://postgres:{Infrastructure.Constants.Defaults.Port}",
                "paired-postgres-container");
        foreach (var candidate in base.BuildRuntimeCandidates(attribute))
            yield return candidate;
    }

    private static string? Value(IDictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? Convert.ToString(value) : null;

    private static string? Concrete(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static DiscoveryCandidate Candidate(string url, string method) =>
        new(url, method, DiscoveryCandidatePriority.Automatic);

    private static string? FirstConcrete(params string?[] values) =>
        values.FirstOrDefault(static value =>
            !string.IsNullOrWhiteSpace(value) &&
            !value.Trim().Equals(Infrastructure.Constants.Configuration.Automatic, StringComparison.OrdinalIgnoreCase));
}
