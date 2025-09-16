namespace Koan.Data.Relational.Orchestration;

public interface IRelationalDdlExecutor
{
    bool TableExists(string schema, string table);
    bool ColumnExists(string schema, string table, string column);
    void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json");
    void CreateTableWithColumns(string schema, string table, List<(string Name, Type ClrType, bool Nullable, bool IsComputed, string? JsonPath, bool IsIndexed)> columns);
    void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted);
    void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable);
    void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique);
}