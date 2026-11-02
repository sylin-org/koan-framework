using System.Threading.Channels;
using Koan.Core.BackgroundServices;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Registry;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Transactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core.Commands;

/// <summary>
/// One queue, one hold per command, in arrival order, on the default route (DATA-0167). Holds keep it correct across processes;
/// the queue gives back-pressure, fairness, announcements in commit order and a stop at a command boundary that answers what is queued.
/// </summary>
public sealed class CommandQueue(ILogger<CommandQueue> logger, IConfiguration configuration, IServiceProvider services)
    : KoanBackgroundServiceBase(logger, configuration)
{
    /// <summary>How many commands wait at most; a sender past this bound waits for a place.</summary>
    public const int QueueBound = 1024;

    private readonly Channel<Pending> _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(QueueBound) { SingleReader = true });
    private volatile Exception? _ended;

    /// <summary>Raised in this process after each commit that wrote, in commit order, on the queue and before the sender is
    /// answered, so a subscriber returns at once. What it throws is logged and changes nothing.</summary>
    public event Action<CommandCommitted>? Committed;

    public override bool IsCritical => true;

    internal async Task<Outcome<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (EntityContext.CurrentTransaction is { IsCompleted: false } open)
            throw new InvalidOperationException(
                $"Sending {command.GetType().Name} from inside transaction '{open.Name}' would wait for a turn that scope keeps from it. " +
                "Do the work in this scope, or send it after this scope ends.");
        var pending = new Pending<TResult>(command);
        try
        {
            await _queue.Writer.WriteAsync(pending, ct);
        }
        catch (Exception unqueued) when (unqueued is ChannelClosedException or OperationCanceledException)
        {
            return new Outcome<TResult>.Failed(DataCommitOutcome.NotDispatched, _ended ?? unqueued);
        }

        try
        {
            return await pending.Answer.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException waiting) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException($"The wait for {command.GetType().Name} was cancelled. It is queued and still runs.", waiting, ct);
        }
    }

    internal Task<Outcome<TResult>>? TrySend<TResult>(ICommand<TResult> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var pending = new Pending<TResult>(command);
        return _queue.Writer.TryWrite(pending) ? pending.Answer.Task : null;
    }

    public override async Task ExecuteCore(CancellationToken stop)
    {
        using var host = AppHost.PushScope(services);
        Exception ended = new OperationCanceledException("The command queue stopped before the command ran.");
        try
        {
            // Only an application with commands answers for the route; the coordinator's refusal is the one a hold would give.
            if (KoanRegistry.GetDiscoveredImplementors(typeof(ICommand)).Length > 0)
                ((TransactionCoordinator)services.GetRequiredService<ITransactionCoordinatorFactory>().CreateHeld(Name)).HoldRoute();
            while (await _queue.Reader.WaitToReadAsync(stop))
                // A stop is honoured at the command boundary: what is still queued never runs.
                while (!stop.IsCancellationRequested && _queue.Reader.TryRead(out var pending)) await pending.Turn(this, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            ended = failure;
            Logger.LogError(failure, "The command queue ended");
            throw;
        }
        finally
        {
            _ended = ended;
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var unrun)) unrun.NeverRan(ended);
        }
    }

    public override Task<HealthReport> Check(CancellationToken ct = default) => Task.FromResult(_ended is { } ended
        ? HealthReport.Unhealthy($"{Name} has ended: {ended.Message}")
        : HealthReport.Healthy($"{Name} is running"));

    private abstract class Pending
    {
        public abstract Task Turn(CommandQueue queue, CancellationToken stop);

        public abstract void NeverRan(Exception cause);
    }

    private sealed class Pending<TResult>(ICommand<TResult> command) : Pending
    {
        public TaskCompletionSource<Outcome<TResult>> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void NeverRan(Exception cause) => Answer.TrySetResult(new Outcome<TResult>.Failed(DataCommitOutcome.NotDispatched, cause));

        // The sender is answered once the scope is gone, so it never continues inside it.
        public override async Task Turn(CommandQueue queue, CancellationToken stop) => Answer.TrySetResult(await Run(queue, stop));

        private async Task<Outcome<TResult>> Run(CommandQueue queue, CancellationToken stop)
        {
            var name = command.GetType().Name;
            var landed = DataCommitOutcome.NotDispatched;
            try
            {
                using (await EntityContext.Hold(name, stop))
                {
                    landed = DataCommitOutcome.NotCommitted;
                    Outcome<TResult> outcome;
                    try
                    {
                        outcome = await command.Run(stop) ?? throw new InvalidOperationException($"{name} returned no outcome.");
                    }
                    catch (Refusal refusal)
                    {
                        outcome = refusal.As<TResult>();
                    }

                    if (outcome is not Outcome<TResult>.Done)
                    {
                        await EntityContext.Rollback(CancellationToken.None);
                        return outcome;
                    }

                    landed = DataCommitOutcome.Unknown;
                    var hold = (TransactionCoordinator)EntityContext.CurrentTransaction!;
                    if (hold.Wrote)
                        hold.AfterCommit(_ =>
                        {
                            queue.Committed?.Invoke(new CommandCommitted(command, hold.Sequences));
                            return Task.CompletedTask;
                        });
                    // The commit is not cut short by a stop: a command that reached it commits or fails on its own terms.
                    await EntityContext.Commit(CancellationToken.None);
                    return outcome;
                }
            }
            catch (Exception failure)
            {
                queue.Logger.LogError(failure, "Command {Command} failed", name);
                // The coordinator reports what it left. Otherwise, a hold that never began leaves a command that never ran.
                return new Outcome<TResult>.Failed(failure is TransactionException reported ? reported.CommitOutcome : landed, failure);
            }
        }
    }
}
