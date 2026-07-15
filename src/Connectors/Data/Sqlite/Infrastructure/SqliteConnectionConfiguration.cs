using Microsoft.Extensions.Configuration;
using Koan.Core;

namespace Koan.Data.Connector.Sqlite.Infrastructure;

/// <summary>Owns the provider-fallback configuration order shared by options, discovery, and boot reporting.</summary>
internal static class SqliteConnectionConfiguration
{
    internal static readonly string[] ProviderFallbackKeys =
    [
        Constants.Configuration.Keys.AltConnectionString,
        Constants.Configuration.Keys.ConnectionString,
        Constants.Configuration.Keys.ConnectionStringsSqlite
    ];

    internal static string? ReadProviderFallback(IConfiguration configuration)
        => Koan.Core.Configuration.ReadFirst(configuration, ProviderFallbackKeys);

    internal static ConfigurationValue<string> ReadProviderFallbackWithSource(
        IConfiguration configuration,
        string defaultValue)
        => Koan.Core.Configuration.ReadFirstWithSource(configuration, defaultValue, ProviderFallbackKeys);

    internal static bool IsAuto(string? value)
        => string.Equals(value?.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}
