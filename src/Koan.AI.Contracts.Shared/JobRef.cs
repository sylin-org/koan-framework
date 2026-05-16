namespace Koan.AI.Contracts.Shared;

/// <summary>
/// Lightweight job identity. Shared across Training, Model.Convert, and Eval contexts.
/// </summary>
public sealed record JobRef(string Id, JobStatus Status)
{
    public override string ToString() => $"Job {Id} [{Status}]";
}
