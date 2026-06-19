namespace Koan.Web.Authorization;

/// <summary>
/// ARCH-0092 (§D) — the canonical action verbs the shared endpoint core maps every operation onto before
/// calling the <see cref="IAuthorize"/> seam. The read/write/remove split is the v1 grain; finer
/// per-operation scope granularity is deferred (ADR "Explicitly deferred").
/// </summary>
public static class EntityAuthorizeActions
{
    public const string Read = "read";
    public const string Write = "write";
    public const string Remove = "remove";
}
