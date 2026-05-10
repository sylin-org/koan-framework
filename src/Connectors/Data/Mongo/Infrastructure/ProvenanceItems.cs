using System.Collections.Generic;
using System.Globalization;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Connector.Mongo;

namespace Koan.Data.Connector.Mongo.Infrastructure;

internal static class MongoProvenanceItems
{
    private static readonly MongoOptions Defaults = new();

    internal static readonly string[] ConnectionStringKeys =
    {
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.ConnectionString),
        ConfigurationConstants.Sources.DefaultMongoConnectionString,
        "ConnectionStrings:Mongo",
        "ConnectionStrings:Default"
    };

    internal static readonly string[] DatabaseKeys =
    {
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.Database),
        ConfigurationConstants.DataFallback.Database
    };

    internal static readonly string[] DefaultPageSizeKeys =
    {
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DefaultPageSize),
        ConfigurationConstants.Sources.DefaultMongoDefaultPageSize
    };

    private static readonly IReadOnlyCollection<string> ConnectionConsumers = new[]
    {
        "Koan.Data.Connector.Mongo.MongoOptionsConfigurator",
        "Koan.Data.Connector.Mongo.MongoClientProvider",
        "Koan.Data.Connector.Mongo.MongoAdapterFactory"
    };

    private static readonly IReadOnlyCollection<string> DatabaseConsumers = new[]
    {
        "Koan.Data.Connector.Mongo.MongoOptionsConfigurator",
        "Koan.Data.Connector.Mongo.MongoClientProvider"
    };

    private static readonly IReadOnlyCollection<string> PagingConsumers = new[]
    {
        "Koan.Data.Connector.Mongo.MongoAdapterFactory"
    };

    internal static readonly ProvenanceItem ConnectionString = new(
        ConnectionStringKeys[0],
        "Mongo Connection String",
        "MongoDB connection string resolved from configuration, discovery, or defaults.",
        IsSecret: false,
        MustSanitize: true,
        DefaultValue: Defaults.ConnectionString,
        DefaultConsumers: ConnectionConsumers);

    internal static readonly ProvenanceItem Database = new(
        DatabaseKeys[0],
        "Mongo Database",
        "Default MongoDB database used for Koan data operations.",
        DefaultValue: Defaults.Database,
        DefaultConsumers: DatabaseConsumers);

    internal static readonly ProvenanceItem EnsureCreatedSupported = new(
        "Mongo.EnsureCreatedSupported",
        "Ensure Created Supported",
        "Indicates whether the Mongo adapter can create missing schema artifacts automatically.",
        DefaultValue: BoolString(true),
        DefaultConsumers: PagingConsumers);

    internal static readonly ProvenanceItem DefaultPageSize = new(
        DefaultPageSizeKeys[0],
        "Default Page Size",
        "Default batch size used when paging Mongo queries.",
        DefaultValue: Defaults.DefaultPageSize.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: PagingConsumers);

    private static string BoolString(bool value) => value ? "true" : "false";
}
