namespace Koan.Data.Relational;

/// <summary>What the schema owner found wrong with one part of a container, and how much it matters.</summary>
public enum RelationalSchemaFindingKind
{
    /// <summary>The store does not hold something the mapping needs.</summary>
    Absent,

    /// <summary>The store holds it, shaped differently than the mapping describes.</summary>
    Drift,

    /// <summary>Neither could be established, because the store cannot describe this to the needed depth.</summary>
    Unverified
}

/// <summary>
/// One thing the schema owner noticed, carrying its own severity.
///
/// <para>Severity travels with the finding rather than with the list it lands in, because whether a difference
/// stops a boot is a policy question answered per column: identity and the structured document cannot drift on
/// any matching mode, a projected column the store computes for the planner's benefit may be absent without
/// anything failing, and <c>Strict</c> tolerates neither. Four parallel string lists could not say that, so
/// each adapter said it privately and said it differently (DATA-0119).</para>
/// </summary>
/// <param name="Subject">The column, index or store-level concern this is about.</param>
/// <param name="Kind">Whether the subject is absent, drifted, or could not be checked.</param>
/// <param name="Detail">What an operator needs to read to act on it.</param>
/// <param name="Corrective">Whether the mapping cannot be served until this is resolved.</param>
public sealed record RelationalSchemaFinding(
    string Subject,
    RelationalSchemaFindingKind Kind,
    string Detail,
    bool Corrective)
{
    public override string ToString() => Detail;
}
