using System.Data;

namespace Koan.Data.Relational.Schema;

public sealed class RelationalSchemaSynchronizer : IRelationalSchemaSynchronizer
{
    public void EnsureCreated(IRelationalDialect dialect, IRelationalSchemaModel model, IDbConnection connection)
    {
        var sql = dialect.CreateTable(model.Table);
        connection.Execute(sql);
        foreach (var ix in dialect.CreateIndexes(model.Table)) connection.Execute(ix);
    }
}