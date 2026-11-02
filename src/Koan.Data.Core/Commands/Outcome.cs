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

    /// <summary>It did not complete. Only <c>Send</c> says so, because a command cannot know: a command that fails throws.</summary>
    public sealed record Failed : Outcome<TResult>
    {
        internal Failed(DataCommitOutcome landed, Exception cause) => (Landed, Cause) = (landed, cause);

        /// <summary><see cref="DataCommitOutcome.NotDispatched"/>: it never ran, because its hold could not begin: the sender gave up, the wait
        /// behind other writers ran out, the route cannot hold, or a scope is open around the send. <see cref="DataCommitOutcome.NotCommitted"/>:
        /// it ran and nothing landed. <see cref="DataCommitOutcome.Unknown"/>: the native commit itself failed.</summary>
        public DataCommitOutcome Landed { get; }

        public Exception Cause { get; }
    }
}
