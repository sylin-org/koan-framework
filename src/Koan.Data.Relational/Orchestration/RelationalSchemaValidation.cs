namespace Koan.Data.Relational;

/// <summary>Definition-level validation of one mapped relational container.</summary>
public sealed record RelationalSchemaValidation
{
    public RelationalSchemaValidation(
        RelationalSchemaPlan plan,
        bool tableExists,
        IEnumerable<string> missing,
        IEnumerable<string> incompatible,
        IEnumerable<string> unverified)
    {
        Plan = plan;
        TableExists = tableExists;
        Missing = Array.AsReadOnly(missing.ToArray());
        Incompatible = Array.AsReadOnly(incompatible.ToArray());
        Unverified = Array.AsReadOnly(unverified.Concat(plan.UnprovedClaims).Distinct(StringComparer.Ordinal).ToArray());
    }

    public RelationalSchemaPlan Plan { get; }
    public bool TableExists { get; }
    public IReadOnlyList<string> Missing { get; }
    public IReadOnlyList<string> Incompatible { get; }
    public IReadOnlyList<string> Unverified { get; }
    public bool IsCompatible => TableExists && Missing.Count == 0 && Incompatible.Count == 0;
    public string State => !IsCompatible ? "Unhealthy" : Unverified.Count == 0 ? "Healthy" : "Degraded";
}
