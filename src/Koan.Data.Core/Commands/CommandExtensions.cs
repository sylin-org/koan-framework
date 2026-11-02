using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core.Commands;

/// <summary>A command's own verbs (ARCH-0135).</summary>
public static class CommandExtensions
{
    /// <summary>How many tries wait at once. Past it a try does not run, so that no stream of them piles up behind a busy store.</summary>
    private const int TryBound = 1024;

    private static int _trying;

    extension<TResult>(ICommand<TResult> command)
    {
        /// <summary>Runs the command in a hold of its own, in this flow and this context, and answers with what happened. Holds take turns, so
        /// this waits behind other writers for at most the store's busy timeout, and is then told <see cref="DataCommitOutcome.NotDispatched"/>;
        /// a route that cannot hold, and an open scope around this send, are told the same. <paramref name="ct"/> ends the wait and the
        /// command, which then leaves nothing, and does not cut a commit short.</summary>
        public async Task<Outcome<TResult>> Send(CancellationToken ct = default)
        {
            var name = command.GetType().Name;
            var landed = DataCommitOutcome.NotDispatched;
            try
            {
                using (await EntityContext.Hold(name, ct))
                {
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
                        outcome = refusal.As<TResult>();
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
                    if (hold.Wrote) hold.BeforeItsTurnEnds = () => Announce(command, hold.Sequences);
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

        /// <summary>Starts the command beside this flow, in this context, and never waits for it. Null, and it never runs, when too many tries are
        /// waiting already. For work a later attempt makes good.</summary>
        public Task<Outcome<TResult>>? TrySend()
        {
            if (Interlocked.Increment(ref _trying) <= TryBound)
                return Task.Run(async () =>
                {
                    try
                    {
                        return await command.Send();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _trying);
                    }
                });
            Interlocked.Decrement(ref _trying);
            return null;
        }
    }

    private static void Announce(object command, IReadOnlyDictionary<Sequence, long> sequences)
    {
        if (AppHost.Current is not { } services) return;
        // A task a watcher starts does not inherit the hold that is ending.
        using (ExecutionContext.SuppressFlow())
            foreach (var watcher in services.GetServices<ICommandWatcher>())
            {
                try
                {
                    watcher.Committed(command, sequences);
                }
                catch (Exception failure)
                {
                    services.GetService<ILogger<ICommandWatcher>>()?.LogError(failure, "A watcher of commands threw for {Command}", command.GetType().Name);
                }
            }
    }
}
