namespace Koan.Data.Relational;

/// <summary>What the store holds, measured against what one mapping needs.</summary>
public sealed record RelationalSchemaValidation
{
    public RelationalSchemaValidation(
        RelationalSchemaPlan plan,
        bool tableExists,
        IEnumerable<RelationalSchemaFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(findings);
        Plan = plan;
        TableExists = tableExists;
        Findings = Array.AsReadOnly(findings.ToArray());
    }

    public RelationalSchemaPlan Plan { get; }
    public bool TableExists { get; }
    public IReadOnlyList<RelationalSchemaFinding> Findings { get; }

    /// <summary>Columns the store does not hold, which is what a repair pass adds.</summary>
    public IReadOnlyList<RelationalSchemaFinding> Absent =>
        Findings.Where(static finding => finding.Kind == RelationalSchemaFindingKind.Absent).ToArray();

    /// <summary>Findings the mapping cannot be served around, which is what stops a boot.</summary>
    public IReadOnlyList<RelationalSchemaFinding> Corrective =>
        Findings.Where(static finding => finding.Corrective).ToArray();

    /// <summary>Whether the store holds everything the mapping describes, exactly as described.</summary>
    public bool IsComplete => TableExists && !Findings.Any(static finding =>
        finding.Kind is RelationalSchemaFindingKind.Absent or RelationalSchemaFindingKind.Drift);

    /// <summary>Whether reads and writes through this mapping will work against the store as it stands.</summary>
    public bool IsServiceable => TableExists && !Findings.Any(static finding => finding.Corrective);

    public string State => !IsServiceable ? "Unhealthy" : Findings.Count == 0 ? "Healthy" : "Degraded";

    /// <summary>
    /// What the schema-validate instruction answers, in one shape for every relational store.
    ///
    /// <para>Each adapter used to compose this itself, and three of them returned <c>TableExists = true</c> and
    /// <c>State = "Healthy"</c> as literals - a health report structurally incapable of reporting ill health.</para>
    /// </summary>
    public IReadOnlyDictionary<string, object?> Report(string provider) => new Dictionary<string, object?>
    {
        ["Provider"] = provider,
        ["Schema"] = Plan.Table.Schema,
        ["Table"] = Plan.Table.Name,
        ["TableExists"] = TableExists,
        ["Columns"] = Plan.Table.Columns.Select(static column => column.Name).ToArray(),
        ["Findings"] = Findings.Select(static finding => finding.Detail).ToArray(),
        ["State"] = State
    };
}
