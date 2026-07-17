using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Core.Infrastructure;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Couchbase.Infrastructure;

namespace Koan.Data.Connector.Couchbase;

/// <summary>
/// Couchbase configuration using autonomous service discovery.
/// Inherits from AdapterOptionsConfigurator for consistent provider patterns.
/// </summary>
internal sealed class CouchbaseOptionsConfigurator : AdapterOptionsConfigurator<CouchbaseOptions>
{
    private readonly IServiceDiscoveryCoordinator? _discoveryCoordinator;

    protected override string ProviderName => "Couchbase";

    public CouchbaseOptionsConfigurator(
        IConfiguration config,
        ILogger<CouchbaseOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
        : base(config, logger, readinessOptions)
    {
        _discoveryCoordinator = discoveryCoordinator;
    }

    // Simplified constructor for orchestration scenarios without DI
    public CouchbaseOptionsConfigurator(IConfiguration config)
        : base(config, NullLogger<CouchbaseOptionsConfigurator>.Instance,
               Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions()))
    {
        _discoveryCoordinator = null;
    }

    protected override void ConfigureProviderSpecific(CouchbaseOptions options)
    {
        LogConfiguration(LogLevel.Debug, "initial",
            ("environment", KoanEnv.EnvironmentName),
            ("orchestrationMode", KoanEnv.OrchestrationMode),
            ("connection", options.ConnectionString));

        // Couchbase-specific configuration
        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsCouchbase,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            LogConfiguration(LogLevel.Information, "explicit");
            options.ConnectionString = NormalizeCouchbaseConnectionString(explicitConnectionString);
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            LogConfiguration(LogLevel.Information, "auto");
            options.ConnectionString = ResolveAutonomousConnection(options.Bucket, options.Username, options.Password);
        }
        else
        {
            LogConfiguration(LogLevel.Information, "preconfigured");
            options.ConnectionString = NormalizeCouchbaseConnectionString(options.ConnectionString);
        }

        options.Bucket = ReadProviderConfiguration(options.Bucket,
            Infrastructure.Constants.Configuration.Keys.Bucket,
            Infrastructure.Constants.Configuration.Keys.AltBucket,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDatabase);

        options.Scope = ReadProviderConfiguration(options.Scope ?? "",
            Infrastructure.Constants.Configuration.Keys.Scope) ?? options.Scope;

        options.Collection = ReadProviderConfiguration(options.Collection ?? "",
            Infrastructure.Constants.Configuration.Keys.Collection) ?? options.Collection;

        options.Username = ReadProviderConfiguration(options.Username ?? "",
            Infrastructure.Constants.Configuration.Keys.Username,
            Infrastructure.Constants.Configuration.Keys.AltUsername) ?? options.Username;

        options.Password = ReadProviderConfiguration(options.Password ?? "",
            Infrastructure.Constants.Configuration.Keys.Password,
            Infrastructure.Constants.Configuration.Keys.AltPassword) ?? options.Password;

        var queryTimeoutSeconds = ReadProviderConfiguration(0,
            Infrastructure.Constants.Configuration.Keys.QueryTimeout);
        if (queryTimeoutSeconds > 0)
        {
            options.QueryTimeout = TimeSpan.FromSeconds(queryTimeoutSeconds);
        }

        options.DurabilityLevel = ReadProviderConfiguration(options.DurabilityLevel ?? "",
            Infrastructure.Constants.Configuration.Keys.DurabilityLevel) ?? options.DurabilityLevel;

        var managementUrl = ReadProviderConfiguration(options.ManagementUrl ?? "",
            Infrastructure.Constants.Configuration.Keys.ManagementUrl);
        if (!string.IsNullOrWhiteSpace(managementUrl))
        {
            options.ManagementUrl = managementUrl;
        }

        LogConfiguration(LogLevel.Information, "final",
            ("connection", options.ConnectionString),
            ("bucket", options.Bucket));
    }

    private string ResolveAutonomousConnection(
        string? bucketName,
        string? username,
        string? password)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                LogDiscovery(LogLevel.Information, "disabled", ("fallback", "couchbase://localhost"));
                return "couchbase://localhost";
            }

            if (_discoveryCoordinator == null)
            {
                LogDiscovery(LogLevel.Warning, "coordinator-missing", ("fallback", "couchbase://localhost"));
                return "couchbase://localhost";
            }

            // Create discovery context with Couchbase-specific parameters
            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = new Dictionary<string, object>()
            };

            if (!string.IsNullOrWhiteSpace(bucketName))
                context.Parameters["bucket"] = bucketName;
            if (!string.IsNullOrWhiteSpace(username))
                context.Parameters["username"] = username;
            if (!string.IsNullOrWhiteSpace(password))
                context.Parameters["password"] = password;

            // Use autonomous discovery coordinator
            var discoveryTask = _discoveryCoordinator.DiscoverService("couchbase", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                LogDiscovery(LogLevel.Information, "success", ("url", result.ServiceUrl));
                return result.ServiceUrl;
            }
            else
            {
                LogDiscovery(LogLevel.Warning, "fallback", ("reason", result.ErrorMessage), ("fallback", "couchbase://localhost"));
                return "couchbase://localhost";
            }
        }
        catch (Exception ex)
        {
            LogDiscovery(LogLevel.Error, "exception", ("error", ex), ("fallback", "couchbase://localhost"));
            return "couchbase://localhost";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false);
    }

    private static string NormalizeCouchbaseConnectionString(string value)
    {
        var trimmed = value?.Trim() ?? "";
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
