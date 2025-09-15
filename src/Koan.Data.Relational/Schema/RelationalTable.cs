namespace Koan.Data.Relational.Schema;

public sealed record RelationalTable(string Name, string? Namespace, RelationalColumn PrimaryKey, IReadOnlyList<RelationalColumn> Columns, IReadOnlyList<RelationalIndex> Indexes);