namespace Koan.Data.Relational.Orchestration;

public sealed class SchemaMismatchException : InvalidOperationException
{
    public SchemaMismatchException(string entity, string table, string policy, string[] missing, string[] extra, bool ddlAllowed)
        : base($"Schema mismatch for {entity} at table {table} under policy {policy}. Missing: [{string.Join(", ", missing)}], Extra: [{string.Join(", ", extra)}]. DdlAllowed={ddlAllowed}.")
    {
        Entity = entity;
        Table = table;
        Policy = policy;
        Missing = missing;
        Extra = extra;
        DdlAllowed = ddlAllowed;
    }

    public string Entity { get; }
    public string Table { get; }
    public string Policy { get; }
    public string[] Missing { get; }
    public string[] Extra { get; }
    public bool DdlAllowed { get; }
}
