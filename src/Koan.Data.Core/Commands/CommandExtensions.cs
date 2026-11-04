using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Transactions;

namespace Koan.Data.Core.Commands;

/// <summary>A command's own verbs (ARCH-0135).</summary>
public static class CommandExtensions
{
    extension<TResult>(ICommand<TResult> command)
    {
        /// <summary>Runs the command in a hold of its own, in this flow and this context, and answers with what happened. Holds take turns, so
        /// this waits behind other writers for at most the store's busy timeout, and is then told <see cref="DataCommitOutcome.NotDispatched"/>;
        /// a route that cannot hold, and an open scope around this send, are told the same. <paramref name="ct"/> ends the wait and the
        /// command, which then leaves nothing, and does not cut a commit short.</summary>
        public Task<Outcome<TResult>> Send(CancellationToken ct = default) => Run(command, clock: null, ct);

        /// <summary>Sends the command as <c>Send(ct)</c> does, running its hold by
        /// <paramref name="clock"/>: what the command and the code it calls read from <see cref="EntityContext.Clock"/>, for this send only.
        /// A test or a seed names the time each write it sends happens at.</summary>
        public Task<Outcome<TResult>> Send(TimeProvider clock, CancellationToken ct = default) =>
            Run(command, clock ?? throw new ArgumentNullException(nameof(clock), "Name the clock this send runs by, or send it with Send(ct), which runs by the host's."), ct);
    }

    private static async Task<Outcome<TResult>> Run<TResult>(ICommand<TResult> command, TimeProvider? clock, CancellationToken ct)
    {
        var name = command.GetType().Name;
        var landed = DataCommitOutcome.NotDispatched;
        try
        {
            using (await EntityContext.Hold(name, ct))
            {
                landed = DataCommitOutcome.NotCommitted;
                var hold = (TransactionCoordinator)EntityContext.CurrentTransaction!;
                if (clock is not null) hold.Clock = clock;
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
}
