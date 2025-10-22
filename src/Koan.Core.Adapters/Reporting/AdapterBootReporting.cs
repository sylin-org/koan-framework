using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core.Adapters.Configuration;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;

namespace Koan.Core.Adapters.Reporting;

/// <summary>
/// Centralized utilities for consistent adapter boot report generation.
/// Eliminates duplicate boot reporting patterns across data adapters.
/// </summary>
public static class AdapterBootReporting
{
    /// <summary>
    /// Reports standard adapter configuration to boot report with consistent formatting.
    /// Handles common adapter capabilities and settings while allowing provider-specific extensions.
    /// </summary>
    /// <typeparam name="TOptions">The adapter options type implementing IAdapterOptions</typeparam>
    /// <param name="module">The provenance module writer</param>
    /// <param name="moduleName">The adapter module name</param>
    /// <param name="moduleVersion">The adapter module version</param>
    /// <param name="options">The configured adapter options</param>
    /// <param name="reportProviderSpecific">Optional callback for provider-specific settings</param>
    public static void ReportAdapterConfiguration<TOptions>(
        this ProvenanceModuleWriter module,
        string moduleName,
        string? moduleVersion,
        TOptions options,
        Action<ProvenanceModuleWriter, TOptions>? reportProviderSpecific = null)
        where TOptions : IAdapterOptions
    {
        module.Describe(moduleVersion);
        // Standard adapter capabilities
        module.AddSetting($"{moduleName}:DefaultPageSize",
            options.DefaultPageSize.ToString(CultureInfo.InvariantCulture));
        module.AddSetting($"{moduleName}:MaxPageSize",
            options.MaxPageSize.ToString(CultureInfo.InvariantCulture));

        // Readiness configuration
        module.AddSetting($"{moduleName}:ReadinessPolicy",
            options.Readiness.Policy.ToString());
        module.AddSetting($"{moduleName}:ReadinessTimeout",
            options.Readiness.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        module.AddSetting($"{moduleName}:ReadinessGating",
            options.Readiness.EnableReadinessGating.ToString());

        // Provider-specific settings via callback
    reportProviderSpecific?.Invoke(module, options);
    }

    /// <summary>
    /// Creates a properly configured adapter options instance for boot reporting.
    /// Handles the common pattern of creating options with default readiness configuration.
    /// </summary>
    /// <typeparam name="TOptions">The adapter options type</typeparam>
    /// <param name="config">The configuration instance</param>
    /// <param name="optionsFactory">Factory function to create the configured options instance</param>
    /// <returns>Configured options instance suitable for boot reporting</returns>
    public static TOptions ConfigureForBootReport<TOptions>(
        IConfiguration config,
        Func<IConfiguration, IOptions<AdaptersReadinessOptions>, TOptions> optionsFactory)
        where TOptions : IAdapterOptions
    {
        var readinessOptions = Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions());
        return optionsFactory(config, readinessOptions);
    }

    /// <summary>
    /// Creates a properly configured adapter options instance using a configurator.
    /// Streamlines the pattern where adapters use configurators to set up options.
    /// </summary>
    /// <typeparam name="TOptions">The adapter options type</typeparam>
    /// <typeparam name="TConfigurator">The configurator type</typeparam>
    /// <param name="config">The configuration instance</param>
    /// <param name="configuratorFactory">Factory function to create the configurator</param>
    /// <param name="optionsFactory">Factory function to create the empty options instance</param>
    /// <returns>Configured options instance suitable for boot reporting</returns>
    public static TOptions ConfigureForBootReportWithConfigurator<TOptions, TConfigurator>(
        IConfiguration config,
        Func<IConfiguration, IOptions<AdaptersReadinessOptions>, TConfigurator> configuratorFactory,
        Func<TOptions> optionsFactory)
        where TOptions : class, IAdapterOptions
        where TConfigurator : IConfigureOptions<TOptions>
    {
        var readinessOptions = Microsoft.Extensions.Options.Options.Create(new AdaptersReadinessOptions());
        var configurator = configuratorFactory(config, readinessOptions);
        var options = optionsFactory();
        configurator.Configure(options);
        return options;
    }

    /// <summary>
    /// Reports connection string information with appropriate security redaction.
    /// Handles the common pattern of reporting connection details while protecting sensitive data.
    /// </summary>
    /// <param name="module">The provenance module writer</param>
    /// <param name="moduleName">The adapter module name</param>
    /// <param name="connectionString">The connection string to report</param>
    /// <param name="settingName">Optional custom setting name (defaults to "ConnectionString")</param>
    public static void ReportConnectionString(
        this ProvenanceModuleWriter module,
        string moduleName,
        string connectionString,
        string settingName = "ConnectionString")
    {
        var redactedConnectionString = Redaction.DeIdentify(connectionString);
        module.AddSetting($"{moduleName}:{settingName}", redactedConnectionString);
    }

    /// <summary>
    /// Reports database/bucket/collection information with consistent naming.
    /// Standardizes how adapters report their storage target information.
    /// </summary>
    /// <param name="module">The provenance module writer</param>
    /// <param name="moduleName">The adapter module name</param>
    /// <param name="database">Database name (can be null)</param>
    /// <param name="container">Container/bucket/collection name (can be null)</param>
    /// <param name="scope">Scope/schema name (can be null)</param>
    public static void ReportStorageTargets(
        this ProvenanceModuleWriter module,
        string moduleName,
        string? database = null,
        string? container = null,
        string? scope = null)
    {
        if (!string.IsNullOrWhiteSpace(database))
            module.AddSetting($"{moduleName}:Database", database);

        if (!string.IsNullOrWhiteSpace(container))
            module.AddSetting($"{moduleName}:Container", container);

        if (!string.IsNullOrWhiteSpace(scope))
            module.AddSetting($"{moduleName}:Scope", scope ?? "<default>");
    }

    /// <summary>
    /// Reports timeout and performance-related settings with consistent formatting.
    /// Centralizes how adapters report their operational timeouts and limits.
    /// </summary>
    /// <param name="module">The provenance module writer</param>
    /// <param name="moduleName">The adapter module name</param>
    /// <param name="queryTimeout">Query timeout (can be null)</param>
    /// <param name="connectionTimeout">Connection timeout (can be null)</param>
    /// <param name="retryCount">Retry attempts (can be null)</param>
    public static void ReportPerformanceSettings(
        this ProvenanceModuleWriter module,
        string moduleName,
        TimeSpan? queryTimeout = null,
        TimeSpan? connectionTimeout = null,
        int? retryCount = null)
    {
        if (queryTimeout.HasValue)
            module.AddSetting($"{moduleName}:QueryTimeout",
                queryTimeout.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture));

        if (connectionTimeout.HasValue)
            module.AddSetting($"{moduleName}:ConnectionTimeout",
                connectionTimeout.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture));

        if (retryCount.HasValue)
            module.AddSetting($"{moduleName}:RetryCount",
                retryCount.Value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Runs autonomous discovery for adapters that support it and returns the resolved connection string.
    /// Falls back to the provided delegate when discovery fails or produces no result.
    /// </summary>
    /// <param name="configuration">The configuration instance to supply to discovery context.</param>
    /// <param name="adapter">The discovery adapter to execute.</param>
    /// <param name="parameters">Optional discovery parameters scoped to the adapter.</param>
    /// <param name="fallback">Delegate returning a fallback connection string when discovery is unavailable.</param>
    /// <param name="healthCheckTimeout">Optional override for health check timeout during discovery.</param>
    /// <returns>A connection string suitable for provenance reporting.</returns>
    public static string ResolveConnectionString(
        IConfiguration? configuration,
        IServiceDiscoveryAdapter adapter,
        IDictionary<string, object>? parameters,
        Func<string> fallback,
        TimeSpan? healthCheckTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(fallback);

        var safeConfiguration = configuration ?? new ConfigurationBuilder().AddInMemoryCollection().Build();
        var context = new DiscoveryContext
        {
            Configuration = safeConfiguration,
            OrchestrationMode = KoanEnv.OrchestrationMode,
            HealthCheckTimeout = healthCheckTimeout ?? TimeSpan.FromMilliseconds(500),
            Parameters = parameters is { Count: > 0 } ? parameters : null
        };

        try
        {
            var result = adapter.DiscoverAsync(context).GetAwaiter().GetResult();
            if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl))
            {
                return result.ServiceUrl!;
            }
        }
        catch
        {
            // Swallow discovery exceptions; fallback handles reporting.
        }

        return fallback();
    }
}