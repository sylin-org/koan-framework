using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core.Provenance;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>
/// Compatibility helpers that preserve the BootReport surface while writing directly to provenance.
/// </summary>
public static class ProvenanceModuleExtensions
{
    public static void AddSetting(
        this ProvenanceModuleWriter module,
        string key,
        string? value,
        bool isSecret = false,
        BootSettingSource source = BootSettingSource.Unknown,
        IReadOnlyCollection<string>? consumers = null,
        string? sourceKey = null,
        ProvenanceSettingState state = ProvenanceSettingState.Configured)
    {
        ArgumentNullException.ThrowIfNull(module);

        module.SetSetting(key, builder =>
        {
            builder
                .Label(key)
                .Description(string.Empty)
                .Value(value)
                .State(state)
                .Source(MapSource(source), sourceKey);

            if (consumers is not null && consumers.Count > 0)
            {
                builder.Consumers(consumers.ToArray());
            }

            if (isSecret)
            {
                builder.Secret(input => Koan.Core.Redaction.DeIdentify(input ?? string.Empty));
            }
        });
    }

    public static void AddSetting(
        this ProvenanceModuleWriter module,
        ProvenanceItem item,
        ProvenancePublicationMode mode,
        object? value,
        IReadOnlyCollection<string>? consumers = null,
        string? sourceKey = null,
        bool usedDefault = false,
        bool? isSecretOverride = null,
        bool? sanitizeOverride = null,
        ProvenanceSettingState? stateOverride = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(item);

        var formatted = item.FormatValue(value);
        var sanitize = sanitizeOverride ?? item.MustSanitize;
        var isSecret = isSecretOverride ?? item.IsSecret;

        if (sanitize && formatted is not null)
        {
            formatted = Koan.Core.Redaction.DeIdentify(formatted);
        }

        var resolvedConsumers = consumers ?? item.DefaultConsumers;
    var source = MapSource(mode);
        var state = stateOverride ?? MapState(mode, usedDefault);
        module.SetSetting(item.Key, builder =>
        {
            builder
                .Label(item.Label)
                .Description(item.Description)
                .Value(formatted)
                .State(state)
                .Source(source, sourceKey);

            if (resolvedConsumers is not null && resolvedConsumers.Count > 0)
            {
                builder.Consumers(resolvedConsumers.ToArray());
            }

            if (isSecret)
            {
                builder.Secret(input => Koan.Core.Redaction.DeIdentify(input ?? string.Empty));
            }
        });
    }

    public static void AddNote(this ProvenanceModuleWriter module, string message)
    {
        ArgumentNullException.ThrowIfNull(module);
        var key = $"note-{Guid.CreateVersion7():n}";
        module.SetNote(key, builder =>
        {
            builder.Message(message ?? string.Empty)
                .Kind(ProvenanceNoteKind.Info);
        });
    }

    public static void AddTool(this ProvenanceModuleWriter module, string name, string route, string? description = null, string? capability = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        module.SetTool(name, builder =>
        {
            builder.Route(route)
                .Description(description)
                .Capability(capability);
        });
    }

    /// <summary>
    /// Publishes a configuration value to provenance with automatic mode resolution.
    /// ARCH-0068: Static helper pattern to eliminate 11 duplicated Publish() methods across connectors.
    /// </summary>
    /// <remarks>
    /// This extension method replaces the private Publish() helper that was duplicated in:
    /// - Data connectors (Postgres, MongoDB, Redis, SQLite, SQL Server)
    /// - AI connectors (Ollama, LMStudio)
    /// - Infrastructure (Data.Backup, Core.Adapters, Web.Auth.Test, Swagger)
    /// </remarks>
    public static void PublishConfigValue<T>(
        this ProvenanceModuleWriter module,
        ProvenanceItem item,
        ConfigurationValue<T> value,
        object? displayOverride = null,
        ProvenancePublicationMode? modeOverride = null,
        bool? usedDefaultOverride = null,
        string? sourceKeyOverride = null,
        bool? sanitizeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(item);

        module.AddSetting(
            item,
            modeOverride ?? ProvenancePublicationModeExtensions.FromConfigurationValue(value),
            displayOverride ?? value.Value,
            sourceKey: sourceKeyOverride ?? value.ResolvedKey,
            usedDefault: usedDefaultOverride ?? value.UsedDefault,
            sanitizeOverride: sanitizeOverride);
    }

    private static ProvenanceSettingSource MapSource(BootSettingSource source)
        => source switch
        {
            BootSettingSource.Auto => ProvenanceSettingSource.Auto,
            BootSettingSource.AppSettings => ProvenanceSettingSource.AppSettings,
            BootSettingSource.Environment => ProvenanceSettingSource.Environment,
            BootSettingSource.LaunchKit => ProvenanceSettingSource.LaunchKit,
            BootSettingSource.Custom => ProvenanceSettingSource.Custom,
            _ => ProvenanceSettingSource.Unknown
        };

    private static ProvenanceSettingSource MapSource(ProvenancePublicationMode mode)
        => mode switch
        {
            ProvenancePublicationMode.Auto => ProvenanceSettingSource.Auto,
            ProvenancePublicationMode.Settings => ProvenanceSettingSource.AppSettings,
            ProvenancePublicationMode.Environment => ProvenanceSettingSource.Environment,
            ProvenancePublicationMode.LaunchKit => ProvenanceSettingSource.LaunchKit,
            ProvenancePublicationMode.Discovery => ProvenanceSettingSource.Custom,
            ProvenancePublicationMode.Custom => ProvenanceSettingSource.Custom,
            _ => ProvenanceSettingSource.Unknown
        };

    private static ProvenanceSettingState MapState(ProvenancePublicationMode mode, bool usedDefault)
        => mode switch
        {
            ProvenancePublicationMode.Discovery => ProvenanceSettingState.Discovered,
            ProvenancePublicationMode.Auto => usedDefault ? ProvenanceSettingState.Default : ProvenanceSettingState.Configured,
            ProvenancePublicationMode.Settings => usedDefault ? ProvenanceSettingState.Default : ProvenanceSettingState.Configured,
            _ => usedDefault ? ProvenanceSettingState.Default : ProvenanceSettingState.Configured
        };
}

/// <summary>
/// Legacy source mapping retained for backwards compatibility with existing registrars.
/// </summary>
public enum BootSettingSource
{
    Unknown,
    Auto,
    AppSettings,
    Environment,
    LaunchKit,
    Custom
}
