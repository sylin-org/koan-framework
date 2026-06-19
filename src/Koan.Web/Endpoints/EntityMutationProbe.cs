namespace Koan.Web.Endpoints;

/// <summary>
/// AN11 (docs/assessment/09 §14) — the well-known <see cref="EntityRequestContext.Items"/> keys that carry
/// dry-run + state-delta data across the protocol-neutral endpoint seam. Defined here (not Koan.Mcp) because
/// <see cref="EntityEndpointService{TEntity,TKey}"/> WRITES them while running the mutation paths; a caller
/// projection (the MCP <c>ResponseTranslator</c>) READS them. dry-run is a first-class endpoint concept, not
/// an MCP-only one — a REST surface can adopt the same flag later.
/// </summary>
public static class EntityMutationProbe
{
    /// <summary>Caller opt-in (bool): compute the pre-mutation "before" state so a state delta can be projected.
    /// MCP sets it on every call; REST stays cost-free (no extra read) until it opts in.</summary>
    public const string WantsDeltaKey = "koan.web.wantsMutationDelta";

    /// <summary>Echoed by the service (bool) when a mutation ran as a rehearsal (nothing was written).</summary>
    public const string DryRunKey = "koan.web.dryRun";

    /// <summary>The pre-mutation entity state (object, or absent when the row did not exist — a create). The
    /// post-mutation "after" state is the result model itself.</summary>
    public const string BeforeKey = "koan.web.mutationBefore";

    /// <summary>The mutation kind for the delta: <c>create</c> | <c>update</c> | <c>delete</c>.</summary>
    public const string OperationKey = "koan.web.mutationOperation";

    /// <summary>For batch mutations (no per-field delta): the count a real run would affect.</summary>
    public const string AffectedCountKey = "koan.web.mutationAffected";
}
