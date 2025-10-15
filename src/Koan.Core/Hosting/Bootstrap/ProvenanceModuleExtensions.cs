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
        string? sourceKey = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        module.SetSetting(key, builder =>
        {
            builder.Value(value)
                .State(ProvenanceSettingState.Configured)
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
