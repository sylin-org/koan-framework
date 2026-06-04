namespace Koan.Jobs;

/// <summary>Per-work-type durability tier — overrides the capability election (JOBS-0005 §8). Prior art: Wolverine's
/// durable-vs-buffered endpoints. At most two ledgers are ever active (in-memory + data-backed); a job routes to
/// the one its resolved mode selects.</summary>
public enum JobPersistenceMode
{
    /// <summary>Default: elect by adapter presence — no durable data adapter → in-memory; durable adapter → data-backed.</summary>
    Auto = 0,
    /// <summary>Force ephemeral: jobs stay in the in-memory ledger even when a durable adapter exists
    /// (high-churn, fire-and-forget work you don't want cluttering the durable store).</summary>
    InMemory = 1,
    /// <summary>Force durable: jobs persist via the data layer; boot warns if no durable adapter is present.</summary>
    DataStore = 2,
}

/// <summary>
/// Declares the durability tier for a work-type's jobs. <c>[JobPersistence(JobPersistenceMode.InMemory)]</c> keeps
/// them ephemeral; <c>JobPersistenceMode.DataStore</c> forces durable. <see cref="Provider"/> mirrors Koan's
/// <c>[DataAdapter("name")]</c> string-provider convention to pin a specific store (reserved — honored in a later phase).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class JobPersistenceAttribute : Attribute
{
    public JobPersistenceAttribute(JobPersistenceMode mode = JobPersistenceMode.Auto) => Mode = mode;

    public JobPersistenceMode Mode { get; }

    /// <summary>Reserved: pin a specific data provider (mirrors <c>[DataAdapter("name")]</c>). Honored in a later phase.</summary>
    public string? Provider { get; set; }
}
