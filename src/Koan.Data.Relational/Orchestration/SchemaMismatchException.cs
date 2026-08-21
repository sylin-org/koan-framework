namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// The store cannot serve a mapping, and adding columns will not make it able to.
///
/// <para>It carries the findings themselves rather than pre-flattened lists, so an operator reading the message
/// and a health surface reading the object see the same thing, and a caller can tell an absent column from one
/// that drifted without parsing prose.</para>
/// </summary>
public sealed class SchemaMismatchException : InvalidOperationException
{
    public SchemaMismatchException(
        string entity,
        RelationalTableDefinition table,
        RelationalSchemaMatchingMode matching,
        IReadOnlyList<RelationalSchemaFinding> findings,
        bool ddlAllowed)
        : base($"Schema mismatch for {entity} at {table} under {matching} matching. " +
               $"{string.Join(" ", findings.Select(static finding => finding.Detail))} DdlAllowed={ddlAllowed}.")
    {
        Entity = entity;
        Table = table;
        Matching = matching;
        Findings = findings;
        DdlAllowed = ddlAllowed;
    }

    public string Entity { get; }
    public RelationalTableDefinition Table { get; }
    public RelationalSchemaMatchingMode Matching { get; }
    public IReadOnlyList<RelationalSchemaFinding> Findings { get; }
    public bool DdlAllowed { get; }
}
