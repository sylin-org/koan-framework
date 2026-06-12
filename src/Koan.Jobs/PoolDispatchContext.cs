namespace Koan.Jobs;

/// <summary>
/// Snapshot of a pool's live member state, resolved by the orchestrator before each claim attempt and passed
/// into <see cref="IJobLedger.ClaimNext"/> (JOBS-0007). The ledger uses it to pick a free member and stamp
/// <see cref="JobRecord.GateKey"/> atomically with the status transition to Running. Keeping this as an input
/// (rather than calling the resolver from inside the ledger) preserves ledger storage-agnosticism.
/// </summary>
public sealed record PoolDispatchContext(string PoolName, IReadOnlyList<string> Members, int CapacityPerMember);
