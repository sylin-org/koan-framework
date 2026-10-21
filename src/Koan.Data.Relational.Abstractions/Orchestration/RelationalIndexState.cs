namespace Koan.Data.Relational.Orchestration;

/// <summary>An index the store already holds: the name it holds it under and the statement that drops it.</summary>
public sealed record RelationalIndexState(string Name, string DropStatement);
