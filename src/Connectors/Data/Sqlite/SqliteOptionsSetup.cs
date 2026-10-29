using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Relational.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteOptionsSetup(IConfiguration configuration) : IConfigureOptions<SqliteOptions>
{
    public void Configure(SqliteOptions options)
    {
        var owner = configuration["Koan:Data:Sources:Default:Adapter"];
        var ownsDefault = string.IsNullOrWhiteSpace(owner) || SqliteAdapterFactory.HandlesProvider(owner);
        var requested = ownsDefault
            ? First(
                Constants.Configuration.Keys.DefaultSourceConnectionString,
                Constants.Configuration.Keys.ProviderSourceConnectionString,
                Constants.Configuration.Keys.ConnectionString,
                Constants.Configuration.Keys.ConnectionStringsSqlite,
                Constants.Configuration.Keys.ConnectionStringsDefault)
            : First(
                Constants.Configuration.Keys.ProviderSourceConnectionString,
                Constants.Configuration.Keys.ConnectionString,
                Constants.Configuration.Keys.ConnectionStringsSqlite);

        var candidate = requested ?? options.ConnectionString;
        options.ConnectionString = IsAuto(candidate)
            ? Constants.DefaultConnection
            : candidate.Trim();
        options.NamingStyle = ReadEnum(options.NamingStyle,
            Constants.Configuration.Keys.ProviderNamingStyle,
            Constants.Configuration.Keys.NamingStyle);
        options.Separator = First(
            Constants.Configuration.Keys.ProviderSeparator,
            Constants.Configuration.Keys.Separator) ?? options.Separator;
        options.DdlPolicy = ReadEnum(options.DdlPolicy,
            Constants.Configuration.Keys.ProviderDdlPolicy,
            Constants.Configuration.Keys.DdlPolicy);
        options.SchemaMatching = ReadEnum(options.SchemaMatching,
            Constants.Configuration.Keys.ProviderSchemaMatching,
            Constants.Configuration.Keys.SchemaMatching);
        options.AllowProductionDdl = options.DdlPolicy == RelationalDdlPolicy.AutoCreate;

        // The posture keys reject what they cannot honour: a misspelt mode must not fall back to the default in silence.
        options.JournalMode = RequireEnum(options.JournalMode,
            Constants.Configuration.Keys.ProviderJournalMode,
            Constants.Configuration.Keys.JournalMode);
        options.Synchronous = RequireEnum(options.Synchronous,
            Constants.Configuration.Keys.ProviderSynchronous,
            Constants.Configuration.Keys.Synchronous);
        options.BusyTimeout = RequireTimeout(options.BusyTimeout,
            Constants.Configuration.Keys.ProviderBusyTimeout,
            Constants.Configuration.Keys.BusyTimeout);
    }

    private T ReadEnum<T>(T fallback, params string[] keys) where T : struct, Enum =>
        Enum.TryParse<T>(First(keys), true, out var value) ? value : fallback;

    private T RequireEnum<T>(T fallback, params string[] keys) where T : struct, Enum
    {
        var (key, text) = FirstWithKey(keys);
        if (text is null) return fallback;
        if (Enum.TryParse<T>(text, true, out var value) && Enum.IsDefined(value)) return value;
        throw new InvalidOperationException(
            $"SQLite configuration '{key}' is '{text}', which is not one of {string.Join(", ", Enum.GetNames<T>())}.");
    }

    private TimeSpan RequireTimeout(TimeSpan fallback, params string[] keys)
    {
        var (key, text) = FirstWithKey(keys);
        if (text is null) return fallback;
        if (TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var value) &&
            value >= TimeSpan.Zero && value.TotalMilliseconds <= int.MaxValue)
            return value;
        throw new InvalidOperationException(
            $"SQLite configuration '{key}' is '{text}', which is not a non-negative time span such as '00:00:30'.");
    }

    private string? First(params string[] keys) => FirstWithKey(keys).Value;

    private (string? Key, string? Value) FirstWithKey(string[] keys)
    {
        foreach (var key in keys)
            if (configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)) return (key, value.Trim());
        return (null, null);
    }

    private static bool IsAuto(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}
