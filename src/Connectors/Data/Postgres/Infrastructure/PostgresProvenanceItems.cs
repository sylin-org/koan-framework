using System.Collections.Generic;
using System.Globalization;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Connector.Postgres;

namespace Koan.Data.Connector.Postgres.Infrastructure;

internal static class PostgresProvenanceItems
{
    private static readonly PostgresOptions Defaults = new();

    private static readonly IReadOnlyCollection<string> ConnectionConsumers = new[]
    {
        "Koan.Data.Connector.Postgres.PostgresOptionsConfigurator",
        "Koan.Data.Connector.Postgres.PostgresAdapterFactory",
        "Koan.Data.Connector.Postgres.Initialization.KoanAutoRegistrar"
    };

    private static readonly IReadOnlyCollection<string> NamingConsumers = new[]
    {
        "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
    };

    private static readonly IReadOnlyCollection<string> SearchPathConsumers = new[]
    {
        "Koan.Data.Connector.Postgres.PostgresOptionsConfigurator",
        "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
    };

    private static readonly IReadOnlyCollection<string> PagingConsumers = new[]
    {
        "Koan.Data.Connector.Postgres.PostgresAdapterFactory"
    };

    internal static readonly ProvenanceItem ConnectionString = new(
        Constants.Configuration.Keys.ConnectionString,
        "Postgres Connection String",
        "PostgreSQL connection string used by the adapter; defaults to discovery when set to auto.",
        MustSanitize: true,
        DefaultValue: Defaults.ConnectionString,
        DefaultConsumers: ConnectionConsumers);

    internal static readonly ProvenanceItem SearchPath = new(
        Constants.Configuration.Keys.SearchPath,
        "Search Path",
        "Default PostgreSQL schema search path applied to connections.",
        DefaultValue: Defaults.SearchPath ?? "public",
        DefaultConsumers: SearchPathConsumers);

    internal static readonly ProvenanceItem NamingStyle = new(
        Constants.Configuration.Keys.NamingStyle,
        "Naming Style",
        "Collection naming strategy applied to generated tables and views.",
        DefaultValue: Defaults.NamingStyle.ToString(),
        DefaultConsumers: NamingConsumers);

    internal static readonly ProvenanceItem Separator = new(
        Constants.Configuration.Keys.Separator,
        "Name Separator",
        "Separator inserted between namespace segments when composing table names.",
        DefaultValue: Defaults.Separator,
        DefaultConsumers: NamingConsumers);

    internal static readonly ProvenanceItem EnsureCreatedSupported = new(
        Constants.Configuration.Keys.EnsureCreatedSupported,
        "EnsureCreated Supported",
        "Indicates whether the adapter supports Create/EnsureCreated semantics.",
        DefaultValue: BoolString(true),
        DefaultConsumers: NamingConsumers);

    internal static readonly ProvenanceItem DefaultPageSize = new(
        Constants.Configuration.Keys.DefaultPageSize,
        "Default Page Size",
        "Default number of rows returned when paging through results.",
        DefaultValue: Defaults.DefaultPageSize.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: PagingConsumers);

    internal static readonly ProvenanceItem MaxPageSize = new(
        Constants.Configuration.Keys.MaxPageSize,
        "Max Page Size",
        "Maximum number of rows allowed per page when paging through results.",
        DefaultValue: Defaults.MaxPageSize.ToString(CultureInfo.InvariantCulture),
        DefaultConsumers: PagingConsumers);

    private static string BoolString(bool value) => value ? "true" : "false";
}
