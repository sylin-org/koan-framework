namespace Koan.Data.Analytics.Runtime;

/// <summary>
/// The closed-generic projection admission a declared question captures, so the name-addressed read-model doors
/// (rows, facets, delta, history) cross the entity's Data boundary without recovering type arguments (DATA-0128).
/// </summary>
internal interface IMaterializedProjectionGate
{
    /// <summary>Admit one read of the projection, or refuse while the caller's reads of its entity are scoped.</summary>
    Task<IAsyncDisposable> EnterProjection(IServiceProvider services, string operation, CancellationToken ct);
}
