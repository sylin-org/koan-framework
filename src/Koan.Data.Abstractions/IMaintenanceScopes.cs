namespace Koan.Data.Abstractions;

/// <summary>
/// The ambient scopes work outside any request runs in. A cross-cutting axis partitions what an entity's rows belong to
/// (a tenant, say), and background maintenance has no caller to name one: it runs its work once per scope, inside it. With
/// no such axis there is one scope, the ambient one.
/// </summary>
public interface IMaintenanceScopes
{
    /// <summary>Runs <paramref name="work"/> once inside each scope, in turn. A failure ends the walk and surfaces.</summary>
    Task ForEach(Func<CancellationToken, Task> work, CancellationToken ct = default);
}
