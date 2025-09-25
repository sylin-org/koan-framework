using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core.Adapters.Configuration;
using Koan.Core.Hosting.Bootstrap;

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
    /// <param name="report">The boot report to add settings to</param>
    /// <param name="moduleName">The adapter module name</param>
    /// <param name="moduleVersion">The adapter module version</param>
    /// <param name="options">The configured adapter options</param>
    /// <param name="reportProviderSpecific">Optional callback for provider-specific settings</param>
    public static void ReportAdapterConfiguration<TOptions>(
        this BootReport report,
        string moduleName,
        string? moduleVersion,
        TOptions options,
        Action<BootReport, TOptions>? reportProviderSpecific = null)
        where TOptions : IAdapterOptions
    {
        report.AddModule(moduleName, moduleVersion);

        // Standard adapter capabilities
        report.AddSetting($"{moduleName}:DefaultPageSize",
            options.DefaultPageSize.ToString(CultureInfo.InvariantCulture));
        report.AddSetting($"{moduleName}:MaxPageSize",
            options.MaxPageSize.ToString(CultureInfo.InvariantCulture));

        // Readiness configuration
        report.AddSetting($"{moduleName}:ReadinessPolicy",
            options.Readiness.Policy.ToString());
        report.AddSetting($"{moduleName}:ReadinessTimeout",
            options.Readiness.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        report.AddSetting($"{moduleName}:ReadinessGating",
            options.Readiness.EnableReadinessGating.ToString());

        // Provider-specific settings via callback
        reportProviderSpecific?.Invoke(report, options);
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
    /// <param name="report">The boot report to add settings to</param>
    /// <param name="moduleName">The adapter module name</param>
    /// <param name="connectionString">The connection string to report</param>
    /// <param name="settingName">Optional custom setting name (defaults to "ConnectionString")</param>
    public static void ReportConnectionString(
        this BootReport report,
        string moduleName,
        string connectionString,
        string settingName = "ConnectionString")
    {
        var redactedConnectionString = Redaction.DeIdentify(connectionString);
        report.AddSetting($"{moduleName}:{settingName}", redactedConnectionString, isSecret: true);
    }

    /// <summary>
    /// Reports database/bucket/collection information with consistent naming.
    /// Standardizes how adapters report their storage target information.
    /// </summary>
    /// <param name="report">The boot report to add settings to</param>
    /// <param name="moduleName">The adapter module name</param>
    /// <param name="database">Database name (can be null)</param>
    /// <param name="container">Container/bucket/collection name (can be null)</param>
    /// <param name="scope">Scope/schema name (can be null)</param>
    public static void ReportStorageTargets(
        this BootReport report,
        string moduleName,
        string? database = null,
        string? container = null,
        string? scope = null)
    {
        if (!string.IsNullOrWhiteSpace(database))
            report.AddSetting($"{moduleName}:Database", database);

        if (!string.IsNullOrWhiteSpace(container))
            report.AddSetting($"{moduleName}:Container", container);

        if (!string.IsNullOrWhiteSpace(scope))
            report.AddSetting($"{moduleName}:Scope", scope ?? "<default>");
    }

    /// <summary>
    /// Reports timeout and performance-related settings with consistent formatting.
    /// Centralizes how adapters report their operational timeouts and limits.
    /// </summary>
    /// <param name="report">The boot report to add settings to</param>
    /// <param name="moduleName">The adapter module name</param>
    /// <param name="queryTimeout">Query timeout (can be null)</param>
    /// <param name="connectionTimeout">Connection timeout (can be null)</param>
    /// <param name="retryCount">Retry attempts (can be null)</param>
    public static void ReportPerformanceSettings(
        this BootReport report,
        string moduleName,
        TimeSpan? queryTimeout = null,
        TimeSpan? connectionTimeout = null,
        int? retryCount = null)
    {
        if (queryTimeout.HasValue)
            report.AddSetting($"{moduleName}:QueryTimeout",
                queryTimeout.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture));

        if (connectionTimeout.HasValue)
            report.AddSetting($"{moduleName}:ConnectionTimeout",
                connectionTimeout.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture));

        if (retryCount.HasValue)
            report.AddSetting($"{moduleName}:RetryCount",
                retryCount.Value.ToString(CultureInfo.InvariantCulture));
    }
}