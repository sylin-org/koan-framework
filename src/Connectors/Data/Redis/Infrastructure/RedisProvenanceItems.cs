using System.Collections.Generic;
using System.Globalization;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Connector.Redis;

namespace Koan.Data.Connector.Redis.Infrastructure;

internal static class RedisProvenanceItems
{
    private const string ConnectionStringKey = Constants.Configuration.Section_Data + ":" + Constants.Configuration.Keys.ConnectionString;
    private const string DatabaseKey = Constants.Configuration.Section_Data + ":" + Constants.Configuration.Keys.Database;
    private const string DefaultPageSizeKey = Constants.Configuration.Section_Data + ":" + Constants.Configuration.Keys.DefaultPageSize;
    private const string MaxPageSizeKey = Constants.Configuration.Section_Data + ":" + Constants.Configuration.Keys.MaxPageSize;
    private const string EnsureCreatedSupportedKey = Constants.Configuration.Section_Data + ":" + Constants.Configuration.Keys.EnsureCreatedSupported;

    private static readonly RedisOptions Defaults = new();

    private static readonly IReadOnlyCollection<string> ConnectionConsumers = new[]
    {
        "Koan.Data.Connector.Redis.RedisOptionsConfigurator",
        "Koan.Data.Connector.Redis.RedisAdapterFactory",
        "Koan.Data.Connector.Redis.Initialization.KoanAutoRegistrar"
    };

    private static readonly IReadOnlyCollection<string> DatabaseConsumers = new[]
    {
        "Koan.Data.Connector.Redis.RedisOptionsConfigurator",
        "StackExchange.Redis.ConnectionMultiplexer"
    };

    private static readonly IReadOnlyCollection<string> PagingConsumers = new[]
    {
        "Koan.Data.Connector.Redis.RedisAdapterFactory"
    };

    internal static readonly ProvenanceItem ConnectionString = new(
        ConnectionStringKey,
        "Redis Connection String",
        "Redis connection string used by the adapter; defaults to discovery when set to auto.",
        MustSanitize: true,
        DefaultValue: Defaults.ConnectionString,
        DefaultConsumers: ConnectionConsumers);

    internal static readonly ProvenanceItem Database = new(
        DatabaseKey,
        "Redis Database",
        "Logical Redis database index used for operations.",
        DefaultValue: Defaults.Database.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: DatabaseConsumers);

    internal static readonly ProvenanceItem EnsureCreatedSupported = new(
        EnsureCreatedSupportedKey,
        "EnsureCreated Supported",
        "Indicates whether the adapter supports Create/EnsureCreated semantics.",
        DefaultValue: BoolString(true),
        DefaultConsumers: PagingConsumers);

    internal static readonly ProvenanceItem DefaultPageSize = new(
        DefaultPageSizeKey,
        "Default Page Size",
        "Default batch size used when paging Redis query results.",
        DefaultValue: Defaults.DefaultPageSize.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: PagingConsumers);

    internal static readonly ProvenanceItem MaxPageSize = new(
        MaxPageSizeKey,
        "Max Page Size",
        "Maximum batch size allowed when paging Redis query results.",
        DefaultValue: Defaults.MaxPageSize.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: PagingConsumers);

    private static string BoolString(bool value) => value ? "true" : "false";
}
