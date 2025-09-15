namespace Koan.Data.Relational.Schema;

public interface IRelationalSchemaSynchronizer
{
    void EnsureCreated(IRelationalDialect dialect, IRelationalSchemaModel model, System.Data.IDbConnection connection);
}