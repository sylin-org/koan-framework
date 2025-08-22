namespace Sora.Data.Relational.Schema;

public sealed record RelationalIndex(string? Name, IReadOnlyList<RelationalColumn> Columns, bool Unique, bool IsPrimaryKey);