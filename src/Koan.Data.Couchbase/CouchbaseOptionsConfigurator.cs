using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Data.Couchbase.Infrastructure;

namespace Koan.Data.Couchbase;

/// <summary>
/// Provides orchestration-aware defaults for <see cref="CouchbaseOptions"/>.
/// </summary>
internal sealed class CouchbaseOptionsConfigurator(
    IConfiguration configuration,
    ILogger<CouchbaseOptionsConfigurator>? logger,
    IOptions<AdaptersReadinessOptions> readinessOptions)
    : IConfigureOptions<CouchbaseOptions>
{
    private readonly AdaptersReadinessOptions _readinessDefaults = readinessOptions.Value;

    public void Configure(CouchbaseOptions options)
    {
        options.ConnectionString = ResolveConnectionString(options.ConnectionString);
        options.Bucket = Configuration.ReadFirst(configuration, options.Bucket,
            Constants.Configuration.Keys.Bucket,
            "Koan:Data:Bucket",
            "ConnectionStrings:Database");

        options.Scope = Configuration.ReadFirst(configuration, options.Scope ?? string.Empty,
            Constants.Configuration.Keys.Scope) ?? options.Scope;

        options.Collection = Configuration.ReadFirst(configuration, options.Collection ?? string.Empty,
            Constants.Configuration.Keys.Collection) ?? options.Collection;

        options.Username = Configuration.ReadFirst(configuration, options.Username ?? string.Empty,
            Constants.Configuration.Keys.Username,
            "Koan:Data:Username") ?? options.Username;

        options.Password = Configuration.ReadFirst(configuration, options.Password ?? string.Empty,
            Constants.Configuration.Keys.Password,
            "Koan:Data:Password") ?? options.Password;

        options.DefaultPageSize = Configuration.ReadFirst(configuration, options.DefaultPageSize,
            Constants.Configuration.Keys.DefaultPageSize);

        options.MaxPageSize = Configuration.ReadFirst(configuration, options.MaxPageSize,
            Constants.Configuration.Keys.MaxPageSize);

        var queryTimeoutSeconds = Configuration.ReadFirst(configuration, 0,
            Constants.Configuration.Keys.QueryTimeout);
        if (queryTimeoutSeconds > 0)
        {
            options.QueryTimeout = TimeSpan.FromSeconds(queryTimeoutSeconds);
        }

        options.DurabilityLevel = Configuration.ReadFirst(configuration, options.DurabilityLevel ?? string.Empty,
            Constants.Configuration.Keys.DurabilityLevel) ?? options.DurabilityLevel;

        // Readiness defaults – allow global policy override with per-adapter tuning
        options.Readiness.Policy = Configuration.ReadFirst(configuration, options.Readiness.Policy,
            "Koan:Data:Couchbase:Readiness:Policy") ?? options.Readiness.Policy;

        var readinessTimeout = Configuration.ReadFirst(configuration, options.Readiness.Timeout,
            "Koan:Data:Couchbase:Readiness:Timeout");
        if (readinessTimeout > TimeSpan.Zero)
        {
            options.Readiness.Timeout = readinessTimeout;
        }
        else if (options.Readiness.Timeout <= TimeSpan.Zero)
        {
            options.Readiness.Timeout = _readinessDefaults.DefaultTimeout;
        }

        options.Readiness.EnableReadinessGating = Configuration.Read(configuration,
            "Koan:Data:Couchbase:Readiness:EnableReadinessGating",
            options.Readiness.EnableReadinessGating);

        if (options.Readiness.Timeout <= TimeSpan.Zero)
        {
            options.Readiness.Timeout = _readinessDefaults.DefaultTimeout;
        }

        logger?.LogInformation("Couchbase configuration resolved. Connection={Connection}, Bucket={Bucket}, Scope={Scope}, Collection={Collection}",
            Redaction.DeIdentify(options.ConnectionString), options.Bucket, options.Scope ?? "<default>", options.Collection ?? "<convention>");
    }

    private string ResolveConnectionString(string? current)
    {
        var configured = Configuration.ReadFirst(configuration, string.Empty,
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
