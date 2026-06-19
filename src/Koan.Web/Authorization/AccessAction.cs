namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§B) — the Constrain axis: the four operation grains the query transform distinguishes. This is a
/// SECOND axis layered over (never replacing) the gate's three-verb <see cref="EntityAuthorizeActions"/>
/// (read/write/remove — the seam + <c>[Access]</c> vocabulary). The split that matters here is
/// <see cref="Create"/> vs <see cref="Update"/>: create has no row, so Constrain STAMPS the owner onto the
/// payload (a <c>Where</c> on create is a silent no-op that lets a forged owner through); update/delete narrow to
/// owned rows. The gate collapses both create and update to "write". Custom projection verbs (Slice C) live on
/// <see cref="AccessGate.Custom"/>, not here.
/// </summary>
public enum AccessAction
{
    /// <summary>List / by-id / query / relationship expansion.</summary>
    Read,

    /// <summary>A new row — stamp the owner onto the payload (no row to narrow).</summary>
    Create,

    /// <summary>An existing row — verify ownership of the loaded row, then (default) freeze the owner.</summary>
    Update,

    /// <summary>Remove — verify ownership of the loaded row; a mass delete is bounded by the same predicate.</summary>
    Delete,
}
