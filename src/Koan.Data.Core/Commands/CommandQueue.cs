using System.Threading.Channels;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Registry;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core.Commands;

/// <summary>
/// One queue, one hold per command, in arrival order (DATA-0167). Holds keep it correct across processes; the queue gives
/// back-pressure, fairness, announcements in commit order and a stop at a command boundary that answers what is queued. Its loop
/// starts with the first send, so a send is answered at any point in a host's life, whatever else has started.
/// </summary>
public sealed class CommandQueue : IHostedService, IHealthContributor, IDisposable
{
    private const int QueueBound = 1024;

    /// <summary>True on the loop's own flow, and on what it runs, none of which can wait for the loop.</summary>
    private static readonly AsyncLocal<bool> OnTheLoop = new();

    private readonly Channel<Pending> _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(QueueBound) { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Lazy<Task> _loop;
    private readonly IServiceProvider _services;
    private readonly ILogger<CommandQueue> _logger;
    private readonly TimeSpan _holdLimit;
    private volatile Exception? _ended;

    internal CommandQueue(IServiceProvider services, ILogger<CommandQueue> logger, TimeSpan holdLimit)
    {
        (_services, _logger, _holdLimit) = (services, logger, holdLimit);
        _loop = new Lazy<Task>(StartLoop);
    }

    /// <summary>Raised in this process after each commit that wrote, in commit order, on the queue and before the sender is
    /// answered, so a subscriber returns at once. What one throws is logged and reaches no other. Work it starts does not run on
    /// the queue's flow, so that work can send.</summary>
    public event Action<CommandCommitted>? Committed;

    string IHealthContributor.Name => nameof(CommandQueue);

    bool IHealthContributor.IsCritical => true;

    Task<HealthReport> IHealthContributor.Check(CancellationToken ct) => Task.FromResult(_ended is { } ended
        ? HealthReport.Unhealthy($"{nameof(CommandQueue)} has ended: {ended.Message}")
        : HealthReport.Healthy($"{nameof(CommandQueue)} is answering"));

    Task IHostedService.StartAsync(CancellationToken ct) => Task.CompletedTask;

    async Task IHostedService.StopAsync(CancellationToken ct)
    {
        _stop.Cancel();
        await _loop.Value.WaitAsync(ct);
    }

    void IDisposable.Dispose() => _stop.Cancel();

    internal async Task<Outcome<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken ct)
    {
        var pending = Admit(command);
        if (EntityContext.CurrentTransaction is { IsCompleted: false } open)
            throw new InvalidOperationException(
                $"Sending {command.GetType().Name} from inside transaction '{open.Name}' would wait for a turn that scope keeps from it. " +
                "Do the work in this scope, or send it after this scope ends.");
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
        var pending = Admit(command);
        return _queue.Writer.TryWrite(pending) ? pending.Answer.Task : null;
    }

    /// <summary>The send's turn in the sender's own context, or the refusal of one sent from the loop, which would wait for itself.</summary>
    private Pending<TResult> Admit<TResult>(ICommand<TResult> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (OnTheLoop.Value)
            throw new InvalidOperationException(
                $"Sending {command.GetType().Name} from a command or from what follows its commit would wait for the queue that is running it. " +
                "Do the work in this command, or send it from a Committed subscriber's own task.");
        _ = _loop.Value;
        return new Pending<TResult>(command, EntityContext.Current ?? new EntityContext.ContextState(), CapturedCarriers.Capture(_services));
    }

    /// <summary>Ends the queue at start when the application has commands and the default route cannot hold, with the refusal a hold would give.</summary>
    internal void RefuseARouteThatCannotHold()
    {
        if (KoanRegistry.GetDiscoveredImplementors(typeof(ICommand)).Length == 0) return;
        try
        {
            ((TransactionCoordinator)_services.GetRequiredService<ITransactionCoordinatorFactory>().CreateHeld(nameof(CommandQueue))).HoldRoute();
        }
        catch (Exception refused)
        {
            _logger.LogError(refused, "The command queue cannot run on this route");
            End(refused);
        }
    }

    private Task StartLoop()
    {
        // The loop inherits nothing of the first sender's flow.
        if (ExecutionContext.IsFlowSuppressed()) return Task.Run(Loop);
        using (ExecutionContext.SuppressFlow()) return Task.Run(Loop);
    }

    private async Task Loop()
    {
        OnTheLoop.Value = true;
        using var host = AppHost.PushScope(_services);
        var stop = _stop.Token;
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stop))
                // A stop is honoured at the command boundary: what is still queued never runs.
                while (!stop.IsCancellationRequested && _queue.Reader.TryRead(out var pending)) await pending.Turn(this, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        finally
        {
            End(new OperationCanceledException("The command queue stopped before the command ran."));
        }
    }

    private void End(Exception cause)
    {
        _ended ??= cause;
        _queue.Writer.TryComplete();
        while (_queue.Reader.TryRead(out var unrun)) unrun.NeverRan(_ended);
    }

    private void Announce(CommandCommitted commit)
    {
        if (Committed is not { } subscribers) return;
        using (ExecutionContext.SuppressFlow())
            foreach (var subscriber in subscribers.GetInvocationList().Cast<Action<CommandCommitted>>())
            {
                try
                {
                    subscriber(commit);
                }
                catch (Exception failure)
                {
                    _logger.LogError(failure, "A subscriber of the command queue threw for {Command}", commit.Command.GetType().Name);
                }
            }
    }

    private abstract class Pending
    {
        public abstract Task Turn(CommandQueue queue, CancellationToken stop);

        public abstract void NeverRan(Exception cause);
    }

    private sealed class Pending<TResult>(ICommand<TResult> command, EntityContext.ContextState context, CapturedCarriers carriers) : Pending
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
                using (carriers.Enter(context))
                using (await EntityContext.Hold(name, stop))
                {
                    landed = DataCommitOutcome.NotCommitted;
                    var hold = (TransactionCoordinator)EntityContext.CurrentTransaction!;
                    var outcome = await RunWithinTheLimit(queue, hold, name, stop);
                    if (outcome is not Outcome<TResult>.Done)
                    {
                        await EntityContext.Rollback(CancellationToken.None);
                        return outcome;
                    }

                    landed = DataCommitOutcome.Unknown;
                    if (hold.Wrote)
                        hold.AfterCommit(_ =>
                        {
                            queue.Announce(new CommandCommitted(command, hold.Sequences));
                            return Task.CompletedTask;
                        });
                    // The commit is not cut short by a stop, and what follows it costs its own answer at most.
                    await EntityContext.Commit(CancellationToken.None).WaitAsync(queue._holdLimit, CancellationToken.None);
                    return outcome;
                }
            }
            catch (Exception failure)
            {
                queue._logger.LogError(failure, "Command {Command} failed", name);
                // Only a commit reports what it left; before it, the phase says.
                return new Outcome<TResult>.Failed(landed == DataCommitOutcome.Unknown && failure is TransactionException reported ? reported.CommitOutcome : landed, failure);
            }
        }

        /// <summary>The command's own part. It is waited for only as long as the hold can last: past that the hold has rolled back and
        /// refuses whatever the command still tries, so a command that never returns costs its own answer and nothing else.</summary>
        private async Task<Outcome<TResult>> RunWithinTheLimit(CommandQueue queue, TransactionCoordinator hold, string name, CancellationToken stop)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(queue._holdLimit);
            hold.EndedByItsOwner = true;
            try
            {
                return await Task.Run(() => command.Run(limit.Token), limit.Token).WaitAsync(limit.Token)
                       ?? throw new InvalidOperationException($"{name} returned no outcome.");
            }
            catch (Refusal refusal)
            {
                return refusal.As<TResult>();
            }
            catch (OperationCanceledException) when (limit.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                throw new TimeoutException($"{name} was still running at the hold's limit of {queue._holdLimit}. Nothing it wrote landed.");
            }
            finally
            {
                hold.EndedByItsOwner = false;
            }
        }
    }
}
