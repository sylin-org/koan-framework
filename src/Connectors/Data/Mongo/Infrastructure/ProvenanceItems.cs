using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Connector.Mongo.Infrastructure;

internal static class MongoProvenanceItems
{
    internal static readonly string[] ConnectionStringKeys =
    {
        "Koan:Data:Mongo:ConnectionString",
        "Koan:Data:Sources:Default:mongo:ConnectionString",
        "ConnectionStrings:Mongo",
        "ConnectionStrings:Default"
    };

    internal static readonly string[] DatabaseKeys =
    {
        "Koan:Data:Mongo:Database",
        "Koan:Data:Database"
    };

    internal static readonly string[] DefaultPageSizeKeys =
    {
        "Koan:Data:Mongo:DefaultPageSize",
        "Koan:Data:Sources:Default:mongo:DefaultPageSize"
    };

    internal static readonly string[] MaxPageSizeKeys =
    {
        "Koan:Data:Mongo:MaxPageSize",
        "Koan:Data:Sources:Default:mongo:MaxPageSize"
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
        DefaultConsumers: ConnectionConsumers);

    internal static readonly ProvenanceItem Database = new(
        DatabaseKeys[0],
        "Mongo Database",
        "Default MongoDB database used for Koan data operations.",
        DefaultConsumers: DatabaseConsumers);

    internal static readonly ProvenanceItem EnsureCreatedSupported = new(
        "Mongo.EnsureCreatedSupported",
        "Ensure Created Supported",
        "Indicates whether the Mongo adapter can create missing schema artifacts automatically.",
        DefaultConsumers: PagingConsumers);

    internal static readonly ProvenanceItem DefaultPageSize = new(
        DefaultPageSizeKeys[0],
        "Default Page Size",
        "Default batch size used when paging Mongo queries.",
        DefaultConsumers: PagingConsumers);

    internal static readonly ProvenanceItem MaxPageSize = new(
        MaxPageSizeKeys[0],
        "Max Page Size",
        "Maximum server-allowed page size for Mongo query batching.",
        DefaultConsumers: PagingConsumers);
}
