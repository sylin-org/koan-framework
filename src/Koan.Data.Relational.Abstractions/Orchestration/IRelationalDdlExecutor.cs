namespace Koan.Data.Relational.Orchestration;

/// <summary>Executes provider-specific relational schema operations.</summary>
public interface IRelationalDdlExecutor
{
    bool TableExists(string schema, string table);
    bool ColumnExists(string schema, string table, string column);
    void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json");
    void CreateTableWithColumns(string schema, string table, IReadOnlyList<RelationalColumnDefinition> columns);
    void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted);
    void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable);
    void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique);
}
