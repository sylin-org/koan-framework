using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Connector.Sqlite.Infrastructure;

internal static class SqliteProvenanceItems
{
    internal static readonly ProvenanceItem ConnectionString = new(
        Constants.Configuration.Keys.ConnectionString,
        "SQLite Connection String",
        "SQLite file or memory connection used by the adapter.",
        MustSanitize: true,
        DefaultValue: "auto",
        DefaultConsumers: ["Koan.Data.Connector.Sqlite.SqliteAdapterFactory"]);
}
