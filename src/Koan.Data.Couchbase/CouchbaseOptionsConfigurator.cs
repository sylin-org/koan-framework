using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Data.Couchbase.Infrastructure;

namespace Koan.Data.Couchbase;

/// <summary>
/// Couchbase adapter configuration using centralized orchestration-aware patterns.
/// Inherits from AdapterOptionsConfigurator to eliminate configuration duplication.
/// </summary>
internal sealed class CouchbaseOptionsConfigurator : AdapterOptionsConfigurator<CouchbaseOptions>
{
    protected override string ProviderName => "Couchbase";

    public CouchbaseOptionsConfigurator(
        IConfiguration configuration,
        ILogger<CouchbaseOptionsConfigurator>? logger,
        IOptions<AdaptersReadinessOptions> readinessOptions)
        : base(configuration, logger, readinessOptions)
    {
    }

    protected override void ConfigureProviderSpecific(CouchbaseOptions options)
    {
        // Couchbase-specific configuration
        options.ConnectionString = ResolveConnectionString(options.ConnectionString);

        options.Bucket = ReadProviderConfiguration(options.Bucket,
            Constants.Configuration.Keys.Bucket,
            "Koan:Data:Bucket",
            "ConnectionStrings:Database");

        options.Scope = ReadProviderConfiguration(options.Scope ?? string.Empty,
            Constants.Configuration.Keys.Scope) ?? options.Scope;

        options.Collection = ReadProviderConfiguration(options.Collection ?? string.Empty,
            Constants.Configuration.Keys.Collection) ?? options.Collection;

        options.Username = ReadProviderConfiguration(options.Username ?? string.Empty,
            Constants.Configuration.Keys.Username,
            "Koan:Data:Username") ?? options.Username;

        options.Password = ReadProviderConfiguration(options.Password ?? string.Empty,
            Constants.Configuration.Keys.Password,
            "Koan:Data:Password") ?? options.Password;

        var queryTimeoutSeconds = ReadProviderConfiguration(0,
            Constants.Configuration.Keys.QueryTimeout);
        if (queryTimeoutSeconds > 0)
        {
            options.QueryTimeout = TimeSpan.FromSeconds(queryTimeoutSeconds);
        }

        options.DurabilityLevel = ReadProviderConfiguration(options.DurabilityLevel ?? string.Empty,
            Constants.Configuration.Keys.DurabilityLevel) ?? options.DurabilityLevel;

        Logger?.LogInformation("Couchbase configuration resolved. Connection={Connection}, Bucket={Bucket}, Scope={Scope}, Collection={Collection}",
            Redaction.DeIdentify(options.ConnectionString), options.Bucket, options.Scope ?? "<default>", options.Collection ?? "<convention>");
    }

    private string ResolveConnectionString(string? current)
    {
        var configured = ReadProviderConfiguration(string.Empty,
            Constants.Configuration.Keys.ConnectionString,
            Constants.Configuration.Keys.AltConnectionString,
            Constants.Configuration.Keys.ConnectionStringsCouchbase,
            Constants.Configuration.Keys.ConnectionStringsDefault);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Normalize(configured!);
        }

        if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return Normalize(current);
        }

        var host = KoanEnv.InContainer ? "couchbase" : "localhost";
        return $"couchbase://{host}";
    }

    private static string Normalize(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed)) return "couchbase://localhost";
        if (trimmed.StartsWith("couchbase://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("couchbases://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Replace("http://", "couchbase://", StringComparison.OrdinalIgnoreCase)
                          .Replace("https://", "couchbases://", StringComparison.OrdinalIgnoreCase);
        }
        return $"couchbase://{trimmed}";
    }
}