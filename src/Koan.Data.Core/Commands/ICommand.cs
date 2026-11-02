using Koan.Core;

namespace Koan.Data.Core.Commands;

/// <summary>Any command: what <see cref="CommandQueue.Committed"/> names, and how Koan discovers that an application has commands.</summary>
[KoanDiscoverable]
public interface ICommand;

/// <summary>
/// One write, as a value (DATA-0167). <c>await new Reply(...).Send(ct)</c> queues it and answers with its outcome. It runs in a
/// hold of its own: it reads and writes through the ordinary Entity verbs, sees its own writes, and commits whole on
/// <see cref="Outcome{TResult}.Done"/> or leaves nothing. It carries every id it creates, and does no slow work, because every
/// other writer of the store waits while it runs.
/// </summary>
public interface ICommand<TResult> : ICommand
{
    /// <summary>Runs in the command's turn. <paramref name="ct"/> is the queue's stop, not the sender's. Any throw but a
    /// <see cref="Refusal"/> becomes <see cref="Outcome{TResult}.Failed"/>.</summary>
    Task<Outcome<TResult>> Run(CancellationToken ct);
}
