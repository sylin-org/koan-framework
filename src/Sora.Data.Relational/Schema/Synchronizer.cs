using Sora.Data.Relational.Schema;
using System.Data;

namespace Sora.Data.Relational.Schema;

public sealed class RelationalSchemaSynchronizer : IRelationalSchemaSynchronizer
{
    public void EnsureCreated(IRelationalDialect dialect, IRelationalSchemaModel model, IDbConnection connection)
    {
        var sql = dialect.CreateTable(model.Table);
        connection.Execute(sql);
        foreach (var ix in dialect.CreateIndexes(model.Table)) connection.Execute(ix);
    }
}

internal static class DbConnectionExtensions
{
    public static int Execute(this IDbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }
}
