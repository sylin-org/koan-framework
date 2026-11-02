using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Core.Commands;

/// <summary>What a sender is told about its command (DATA-0167). Only <see cref="Done"/> commits; every other outcome leaves nothing.</summary>
public abstract record Outcome<TResult>
{
    private Outcome() { }

    /// <summary>It committed, and this is its result.</summary>
    public sealed record Done(TResult Result) : Outcome<TResult>;

    /// <summary>What it acts on does not exist, or this sender may not know that it does.</summary>
    public sealed record Absent : Outcome<TResult>;

    /// <summary>This sender may not do it, or not now, and this says why.</summary>
    public sealed record Gated(string Why) : Outcome<TResult>;

    /// <summary>This field's value cannot be taken, and this says why.</summary>
    public sealed record Invalid(string Field, string Reason) : Outcome<TResult>;

    /// <summary>It disagrees with what is already there.</summary>
    public sealed record Conflict(string Reason) : Outcome<TResult>;

    /// <summary>It did not complete. <see cref="DataCommitOutcome.NotDispatched"/>: it never ran, because it was stopped in the
    /// queue, its sender gave up before reaching it, or its route cannot hold. <see cref="DataCommitOutcome.NotCommitted"/>: nothing
    /// landed. <see cref="DataCommitOutcome.Unknown"/>: the native commit itself failed.</summary>
    public sealed record Failed(DataCommitOutcome Landed, Exception Cause) : Outcome<TResult>;
}
