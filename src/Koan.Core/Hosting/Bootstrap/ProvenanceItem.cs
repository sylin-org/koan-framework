using System;
using System.Collections.Generic;
using System.Globalization;
using Koan.Core.Provenance;
using Koan.Core;

namespace Koan.Core.Hosting.Bootstrap;

/// <summary>
/// Rich descriptor for provenance-aware configuration facts.
/// </summary>
public sealed record class ProvenanceItem(
    string Key,
    string Label,
    string Description,
    bool IsSecret = false,
    bool MustSanitize = false,
    string? DefaultValue = null,
    IReadOnlyCollection<string>? AcceptableValues = null,
    string? DocumentationLink = null,
    IReadOnlyCollection<string>? DefaultConsumers = null)
{
    internal string? FormatValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is string str)
        {
            return str;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        if (value is IEnumerable<string> strEnumerable)
        {
            return string.Join(", ", strEnumerable);
        }

        return value.ToString();
    }
}

public enum ProvenancePublicationMode
{
    Auto,
    Settings,
    Environment,
    LaunchKit,
    Custom,
    Discovery
}

public static class ProvenancePublicationModeExtensions
{
    public static ProvenancePublicationMode FromConfigurationValue<T>(ConfigurationValue<T> value)
        => FromBootSource(value.Source, value.UsedDefault);

    public static ProvenancePublicationMode ToPublicationMode<T>(this ConfigurationValue<T> value)
        => FromConfigurationValue(value);

    public static ProvenancePublicationMode FromBootSource(BootSettingSource source, bool usedDefault = false)
    {
        if (usedDefault)
        {
            return ProvenancePublicationMode.Auto;
        }

        return source switch
        {
            BootSettingSource.AppSettings => ProvenancePublicationMode.Settings,
            BootSettingSource.Environment => ProvenancePublicationMode.Environment,
            BootSettingSource.LaunchKit => ProvenancePublicationMode.LaunchKit,
            BootSettingSource.Custom => ProvenancePublicationMode.Custom,
            BootSettingSource.Auto => ProvenancePublicationMode.Auto,
            _ => ProvenancePublicationMode.Auto
        };
    }
}
