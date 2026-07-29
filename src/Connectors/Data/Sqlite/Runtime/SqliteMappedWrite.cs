namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed record SqliteMappedWrite(
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlySet<string> NestedRoots);
