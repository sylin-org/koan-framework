namespace Koan.Data.Relational.Schema;

public sealed class RelationalSchemaModel : IRelationalSchemaModel
{
    public RelationalSchemaModel(RelationalTable table) => Table = table;
    public RelationalTable Table { get; }
}