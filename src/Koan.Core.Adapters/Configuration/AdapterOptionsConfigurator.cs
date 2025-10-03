using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core.Logging;

namespace Koan.Core.Adapters.Configuration;

/// <summary>
/// Base configurator for data adapter options that centralizes common configuration patterns.
/// Eliminates duplication across adapters for readiness, paging, and configuration key resolution.
/// </summary>
/// <typeparam name="TOptions">The adapter-specific options type that implements IAdapterOptions</typeparam>
public abstract class AdapterOptionsConfigurator<TOptions> : IConfigureOptions<TOptions>
    where TOptions : class, IAdapterOptions
{
    protected IConfiguration Configuration { get; }
    protected ILogger? Logger { get; }
    protected AdaptersReadinessOptions ReadinessDefaults { get; }

    /// <summary>
    /// Provider name used for configuration key resolution (e.g., "Mongo", "Couchbase")
    /// </summary>
    protected abstract string ProviderName { get; }

    protected AdapterOptionsConfigurator(
        IConfiguration config,
        ILogger? logger,
        IOptions<AdaptersReadinessOptions> readiness)
    {
        Configuration = config;
        Logger = logger;
        ReadinessDefaults = readiness.Value;
    }

    public void Configure(TOptions options)
    {
        KoanLog.ConfigDebug(Logger, LogActions.ConfigurationLifecycle, LogOutcomes.Start, ("provider", ProviderName));

        // Configure provider-specific settings first
        ConfigureProviderSpecific(options);

        // Apply common configuration patterns
        ConfigureReadiness(options.Readiness);
        ConfigurePaging(options);

        KoanLog.ConfigDebug(Logger, LogActions.ConfigurationLifecycle, LogOutcomes.Complete, ("provider", ProviderName));
    }

    /// <summary>
    /// Override to implement provider-specific configuration logic.
    /// Common patterns (readiness, paging) are handled by the base class.
    /// </summary>
    /// <param name="options">The adapter options to configure</param>
    protected abstract void ConfigureProviderSpecific(TOptions options);

    /// <summary>
    /// Centralizes readiness configuration with proper type conversion and fallbacks
    /// </summary>
    protected void ConfigureReadiness(IAdapterReadinessConfiguration readiness)
    {
        // Cast to concrete type to enable property setting
        if (readiness is not AdapterReadinessConfiguration config)
        {
            KoanLog.ConfigWarning(Logger, LogActions.ReadinessConfiguration, LogOutcomes.Skipped,
                ("provider", ProviderName),
                ("reason", "adapter-readiness-configuration-not-used"));
            return;
        }

        // Policy configuration with enum parsing
        var policyStr = Core.Configuration.ReadFirst(Configuration, config.Policy.ToString(),
            $"Koan:Data:{ProviderName}:Readiness:Policy",
            "Koan:Data:Readiness:Policy");

        if (Enum.TryParse<ReadinessPolicy>(policyStr, out var policy))
        {
            config.Policy = policy;
        }

        // Timeout configuration with TimeSpan conversion
        var timeoutSecondsStr = Core.Configuration.ReadFirst(Configuration,
            ((int)config.Timeout.TotalSeconds).ToString(),
            $"Koan:Data:{ProviderName}:Readiness:Timeout",
            "Koan:Data:Readiness:Timeout");

        if (int.TryParse(timeoutSecondsStr, out var timeoutSeconds) && timeoutSeconds > 0)
        {
            config.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }
        else if (config.Timeout <= TimeSpan.Zero)
        {
            config.Timeout = ReadinessDefaults.DefaultTimeout;
        }

        // Gating configuration
        config.EnableReadinessGating = Core.Configuration.Read(Configuration,
            $"Koan:Data:{ProviderName}:Readiness:EnableReadinessGating",
            config.EnableReadinessGating);

        KoanLog.ConfigDebug(Logger, LogActions.ReadinessConfiguration, LogOutcomes.Applied,
            ("provider", ProviderName),
            ("policy", config.Policy),
            ("timeoutSeconds", config.Timeout.TotalSeconds),
            ("gating", config.EnableReadinessGating));
    }

    /// <summary>
    /// Centralizes paging configuration with consistent key patterns
    /// </summary>
    protected void ConfigurePaging(IAdapterOptions options)
    {
        options.DefaultPageSize = Core.Configuration.ReadFirst(Configuration, options.DefaultPageSize,
            $"Koan:Data:{ProviderName}:DefaultPageSize",
            "Koan:Data:DefaultPageSize");

        options.MaxPageSize = Core.Configuration.ReadFirst(Configuration, options.MaxPageSize,
            $"Koan:Data:{ProviderName}:MaxPageSize",
            "Koan:Data:MaxPageSize");

        KoanLog.ConfigDebug(Logger, LogActions.PagingConfiguration, LogOutcomes.Applied,
            ("provider", ProviderName),
            ("defaultPageSize", options.DefaultPageSize),
            ("maxPageSize", options.MaxPageSize));
    }

    /// <summary>
    /// Helper method for provider-specific configuration key resolution
    /// </summary>
    protected T ReadProviderConfiguration<T>(T defaultValue, params string[] keys)
    {
        var providerKeys = new string[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            providerKeys[i] = keys[i].Replace("{Provider}", ProviderName);
        }

        // Use the overload that takes params string[]
        if (typeof(T) == typeof(string))
        {
            var defaultString = defaultValue as string;
            var result = Core.Configuration.ReadFirst(Configuration, providerKeys) ?? defaultString;
            return (T)(object)(result ?? string.Empty);
        }
        else if (typeof(T) == typeof(int))
        {
            var result = Core.Configuration.ReadFirst(Configuration, (int)(object)defaultValue!, providerKeys);
            return (T)(object)result;
        }
        else if (typeof(T) == typeof(bool))
        {
            var result = Core.Configuration.ReadFirst(Configuration, (bool)(object)defaultValue!, providerKeys);
            return (T)(object)result;
        }
        else
        {
            // Fallback - try to read as string and convert
            var stringResult = Core.Configuration.ReadFirst(Configuration, defaultValue?.ToString() ?? string.Empty, providerKeys);
            try
            {
                return (T)Convert.ChangeType(stringResult, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    private static class LogActions
    {
        public const string ConfigurationLifecycle = "adapter.config.lifecycle";
        public const string ReadinessConfiguration = "adapter.config.readiness";
        public const string PagingConfiguration = "adapter.config.paging";
    }

    protected static class LogOutcomes
    {
        public const string Start = "start";
        public const string Complete = "complete";
        public const string Applied = "applied";
        public const string Skipped = "skipped";
    }

}