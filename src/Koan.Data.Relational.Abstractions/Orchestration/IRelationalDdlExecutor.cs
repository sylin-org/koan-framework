namespace Koan.Data.Relational.Orchestration;

/// <summary>Executes provider-specific relational schema operations.</summary>
public interface IRelationalDdlExecutor
{
    bool TableExists(string schema, string table);
    bool ColumnExists(string schema, string table, string column);
    RelationalColumnDefinition? DescribeColumn(string schema, string table, string column)
        => ColumnExists(schema, table, column)
            ? new RelationalColumnDefinition(column, typeof(object), true)
            : null;
    void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json");
    void CreateTableWithColumns(string schema, string table, IReadOnlyList<RelationalColumnDefinition> columns);
    void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted);
    void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable);
    void AddMappedColumn(string schema, string table, RelationalColumnDefinition column)
        => AddPhysicalColumn(schema, table, column.Name, column.ClrType, column.Nullable);
    void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique);
    void CreateJsonExpressionIndex(
        string schema,
        string table,
        string indexName,
        IReadOnlyList<RelationalJsonIndexPart> parts,
        bool unique);
    void CreateMappedIndex(string schema, string table, RelationalIndexDefinition index)
    {
        if (index.Ttl)
            throw new NotSupportedException("This relational DDL executor does not implement native TTL lowering.");
        if (index.Parts.All(static part => !part.IsNested))
        {
            CreateIndex(schema, table, index.Name, index.Parts.Select(static part => part.Name).ToArray(), index.Unique);
            return;
        }
        CreateJsonExpressionIndex(
            schema,
            table,
            index.Name,
            index.Parts.Select(part => new RelationalJsonIndexPart(
                part.Name,
                part.IsNested ? "$." + string.Join('.', part.Segments) : null)).ToArray(),
            index.Unique);
    }
}
