using System;

namespace Koan.Jobs.Model;

/// <summary>
/// Read-only, non-generic view of a job's shared runtime state (JOBS-0003). Used for job-type
/// discovery (the registry scans for implementors) and for observability surfaces (event publisher)
/// that only read the common fields. Every <see cref="Job{T}"/> implements it via its public
/// properties; the generic runtime mutates jobs through their concrete type, not this view.
/// </summary>
public interface IKoanJob
{
    string Id { get; }
    JobStatus Status { get; }
    string? CorrelationId { get; }
    double Progress { get; }
    string? ProgressMessage { get; }
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset? CompletedAt { get; }
    string? LastError { get; }
}

/// <summary>Typed job contract (JOBS-0003): pins the CRTP self-type for generic binding and
/// <see cref="JobRef.For{T}"/>.</summary>
public interface IKoanJob<T> : IKoanJob where T : Job<T>, new()
{
}
