using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Connector.SqlServer;

namespace Koan.Data.Connector.SqlServer.Infrastructure;

internal static class SqlServerProvenanceItems
{
    private static readonly SqlServerOptions Defaults = new();

    private static readonly IReadOnlyCollection<string> ConnectionConsumers = new[]
    {
        "Koan.Data.Connector.SqlServer.SqlServerOptionsConfigurator",
        "Koan.Data.Connector.SqlServer.SqlServerAdapterFactory"
    };

    private static readonly IReadOnlyCollection<string> NamingConsumers = new[]
    {
        "Koan.Data.Connector.SqlServer.SqlServerAdapterFactory"
    };

    internal static readonly ProvenanceItem ConnectionString = new(
        Constants.Configuration.Keys.ConnectionString,
        "SQL Server Connection String",
        "SQL Server connection string used by the adapter; defaults to discovery when set to auto.",
        MustSanitize: true,
        DefaultValue: Defaults.ConnectionString,
        DefaultConsumers: ConnectionConsumers);

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

    private static string BoolString(bool value) => value ? "true" : "false";
}
