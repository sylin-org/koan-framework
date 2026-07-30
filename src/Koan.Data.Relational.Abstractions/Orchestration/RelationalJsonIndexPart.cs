namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// Describes one part of an index over the canonical relational JSON envelope. A null
/// <see cref="JsonPath"/> identifies a physical envelope column such as <c>Id</c>.
/// </summary>
public sealed record RelationalJsonIndexPart(string ColumnName, string? JsonPath);
