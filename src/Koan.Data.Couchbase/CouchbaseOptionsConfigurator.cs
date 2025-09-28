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
using Koan.Data.Couchbase.Infrastructure;

namespace Koan.Data.Couchbase;

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
        Logger?.LogInformation("Couchbase Orchestration-Aware Configuration Started");
        Logger?.LogInformation("Environment: {Environment}, OrchestrationMode: {OrchestrationMode}",
            KoanEnv.EnvironmentName, KoanEnv.OrchestrationMode);
        Logger?.LogInformation("Initial options - ConnectionString: '{ConnectionString}'",
            options.ConnectionString);

        // Couchbase-specific configuration
        var explicitConnectionString = ReadProviderConfiguration("",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsCouchbase,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            Logger?.LogInformation("Using explicit connection string from configuration");
            options.ConnectionString = NormalizeCouchbaseConnectionString(explicitConnectionString);
        }
        else if (string.Equals(options.ConnectionString?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Logger?.LogInformation("Auto-detection mode - using autonomous service discovery");
            options.ConnectionString = ResolveAutonomousConnection(options.Bucket, options.Username, options.Password, Logger);
        }
        else
        {
            Logger?.LogInformation("Using pre-configured connection string");
            options.ConnectionString = NormalizeCouchbaseConnectionString(options.ConnectionString);
        }

        options.Bucket = ReadProviderConfiguration(options.Bucket,
            Infrastructure.Constants.Configuration.Keys.Bucket,
            "Koan:Data:Bucket",
            "ConnectionStrings:Database");

        options.Scope = ReadProviderConfiguration(options.Scope ?? string.Empty,
            Infrastructure.Constants.Configuration.Keys.Scope) ?? options.Scope;

        options.Collection = ReadProviderConfiguration(options.Collection ?? string.Empty,
            Infrastructure.Constants.Configuration.Keys.Collection) ?? options.Collection;

        options.Username = ReadProviderConfiguration(options.Username ?? string.Empty,
            Infrastructure.Constants.Configuration.Keys.Username,
            "Koan:Data:Username") ?? options.Username;

        options.Password = ReadProviderConfiguration(options.Password ?? string.Empty,
            Infrastructure.Constants.Configuration.Keys.Password,
            "Koan:Data:Password") ?? options.Password;

        var queryTimeoutSeconds = ReadProviderConfiguration(0,
            Infrastructure.Constants.Configuration.Keys.QueryTimeout);
        if (queryTimeoutSeconds > 0)
        {
            options.QueryTimeout = TimeSpan.FromSeconds(queryTimeoutSeconds);
        }

        options.DurabilityLevel = ReadProviderConfiguration(options.DurabilityLevel ?? string.Empty,
            Infrastructure.Constants.Configuration.Keys.DurabilityLevel) ?? options.DurabilityLevel;

        Logger?.LogInformation("Final Couchbase Configuration");
        Logger?.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        Logger?.LogInformation("Bucket: {Bucket}", options.Bucket);
        Logger?.LogInformation("Couchbase Orchestration-Aware Configuration Complete");
    }

    private string ResolveAutonomousConnection(
        string? bucketName,
        string? username,
        string? password,
        ILogger? logger)
    {
        try
        {
            if (IsAutoDetectionDisabled())
            {
                logger?.LogInformation("Auto-detection disabled via configuration - using localhost");
                return "couchbase://localhost";
            }

            if (_discoveryCoordinator == null)
            {
                logger?.LogWarning("Service discovery coordinator not available, falling back to localhost");
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
            var discoveryTask = _discoveryCoordinator.DiscoverServiceAsync("couchbase", context);
            var result = discoveryTask.GetAwaiter().GetResult();

            if (result.IsSuccessful)
            {
                logger?.LogInformation("Couchbase discovered via autonomous discovery: {ServiceUrl}", result.ServiceUrl);
                return result.ServiceUrl;
            }
            else
            {
                logger?.LogWarning("Autonomous Couchbase discovery failed, falling back to localhost");
                return "couchbase://localhost";
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error in autonomous Couchbase discovery, falling back to localhost");
            return "couchbase://localhost";
        }
    }

    private bool IsAutoDetectionDisabled()
    {
        return Koan.Core.Configuration.Read(Configuration, "Koan:Data:Couchbase:DisableAutoDetection", false);
    }

    private static string NormalizeCouchbaseConnectionString(string value)
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