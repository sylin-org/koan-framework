namespace Koan.Data.Abstractions;

/// <summary>
/// ARCH-0100: a generic, axis-agnostic marker that opts an entity <b>out of all ambient-axis composition</b> —
/// no cross-cutting axis (tenant, classification, …) may stamp or scope it. It is the tenancy-free signal that
/// infrastructure entities (e.g. the <c>Koan.Jobs</c> ledger rows) use so the restored ambient never tenant-stamps
/// them and claim-time reads never hit a tenant filter — without taking a dependency on <c>Koan.Tenancy</c> or
/// naming an axis.
///
/// <para>Every ambient-axis seam excludes a type that implements this marker. <c>Koan.Tenancy</c> keeps its own
/// <c>[HostScoped]</c> control-plane attribute and treats a type as exempt if it carries <c>[HostScoped]</c>
/// <i>or</i> implements <see cref="IAmbientExempt"/> (a union); a future classification axis does likewise. The
/// marker only suppresses the ambient managed <i>field</i> (a JSON-leaf/column), not the storage-set/partition —
/// so applying it moves no rows.</para>
/// </summary>
public interface IAmbientExempt
{
}
