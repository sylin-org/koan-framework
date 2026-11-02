using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core.Commands;

/// <summary>A command's own verbs (ARCH-0135).</summary>
public static class CommandExtensions
{
    /// <summary>How many tries wait for their hold at once. Past it a try does not run, so that no stream of them piles up behind a busy store.
    /// A slot lasts until the hold begins or the try is told it never ran, so at most the store's busy timeout.</summary>
    private const int TryBound = 1024;

    private static int _trying;

    extension<TResult>(ICommand<TResult> command)
    {
        /// <summary>Runs the command in a hold of its own, in this flow and this context, and answers with what happened. Holds take turns, so
        /// this waits behind other writers for at most the store's busy timeout, and is then told <see cref="DataCommitOutcome.NotDispatched"/>;
        /// a route that cannot hold, and an open scope around this send, are told the same. <paramref name="ct"/> ends the wait and the
        /// command, which then leaves nothing, and does not cut a commit short.</summary>
        public Task<Outcome<TResult>> Send(CancellationToken ct = default) => Run(command, ct, began: null);

        /// <summary>Starts the command beside this flow, in this context, and never waits for it. Null, and it never runs, when too many tries are
        /// waiting already. For work a later attempt makes good.</summary>
        public Task<Outcome<TResult>>? TrySend()
        {
            if (Interlocked.Increment(ref _trying) > TryBound)
            {
                Interlocked.Decrement(ref _trying);
                return null;
            }

            var released = 0;
            void Release()
            {
                if (Interlocked.Exchange(ref released, 1) == 0) Interlocked.Decrement(ref _trying);
            }

            return Task.Run(async () =>
            {
                try
                {
                    return await Run(command, CancellationToken.None, Release);
                }
                finally
                {
                    Release();
                }
            });
        }
    }

    private static async Task<Outcome<TResult>> Run<TResult>(ICommand<TResult> command, CancellationToken ct, Action? began)
    {
        var name = command.GetType().Name;
        var landed = DataCommitOutcome.NotDispatched;
        // The sender's context, from before its hold: what a watcher runs in.
        var sender = ExecutionContext.Capture();
        try
        {
            using (await EntityContext.Hold(name, ct))
            {
                began?.Invoke();
                landed = DataCommitOutcome.NotCommitted;
                var hold = (TransactionCoordinator)EntityContext.CurrentTransaction!;
                Outcome<TResult> outcome;
                hold.EndedByItsOwner = true;
                try
                {
                    outcome = await command.Run(ct) ?? throw new InvalidOperationException($"{name} returned no outcome.");
                }
                catch (Refusal refusal)
                {
                    outcome = new Outcome<TResult>.Refused(refusal.Reason);
                }
                finally
                {
                    hold.EndedByItsOwner = false;
                }

                if (outcome is not Outcome<TResult>.Done)
                {
                    await EntityContext.Rollback(CancellationToken.None);
                    return outcome;
                }

                landed = DataCommitOutcome.Unknown;
                if (hold.Wrote) hold.BeforeItsTurnEnds = () => Announce(command, hold.Sequences, sender);
                await EntityContext.Commit(CancellationToken.None);
                return outcome;
            }
        }
        catch (Exception failure)
        {
            // Only a commit reports what it left; before it, the phase says.
            return new Outcome<TResult>.Failed(landed == DataCommitOutcome.Unknown && failure is TransactionException reported ? reported.CommitOutcome : landed, failure);
        }
    }

    /// <summary>Tells the watchers, in the sender's context and not the hold's, which has committed: a send one of them starts takes its turn behind this one.</summary>
    private static void Announce(object command, IReadOnlyDictionary<Sequence, long> sequences, ExecutionContext? sender)
    {
        if (AppHost.Current is not { } services) return;
        var watchers = services.GetServices<ICommandWatcher>();
        foreach (var watcher in watchers)
        {
            try
            {
                if (sender is null) watcher.Committed(command, sequences);
                else ExecutionContext.Run(sender, _ => watcher.Committed(command, sequences), null);
            }
            catch (Exception failure)
            {
                services.GetService<ILogger<ICommandWatcher>>()?.LogError(failure, "A watcher of commands threw for {Command}", command.GetType().Name);
            }
        }
    }
}
