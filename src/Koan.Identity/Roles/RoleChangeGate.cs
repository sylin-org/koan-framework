using System.Diagnostics;
using System.Runtime.CompilerServices;
using Koan.Data.Core;
using Koan.Data.Core.Transactions;

namespace Koan.Identity.Roles;

/// <summary>
/// What every role, member and overwrite change passes before <see cref="RoleCollection"/>'s other locks (SEC-0036). A
/// change outside any scope passes with others like it. A hold passes alone, at its first change, and stays until it ends,
/// so no other flow of this process can decide a change on committed state that the hold is about to replace.
/// <para>A hold's first change comes in holding the store's write lock, and a change already under way outside a scope may
/// be waiting for that same lock, so waiting for it can be futile. That wait alone is bounded, by
/// <see cref="RoleOptions.HeldChangeWait"/>, and past it the change refuses, leaving nothing of itself; the hold's rollback
/// then frees the store, and the other change lands. Every other wait here ends with a hold, which its limit bounds.</para>
/// </summary>
internal sealed class RoleChangeGate(TimeSpan heldChangeWait)
{
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly ConditionalWeakTable<ITransactionCoordinator, HeldChanges> _holds = new();
    private readonly object _sync = new();
    private int _underWay;
    private TaskCompletionSource? _drained;

    /// <summary>The hold the calling flow is in, while it is open.</summary>
    public static ITransactionCoordinator? CurrentHold =>
        EntityContext.CurrentTransaction is { IsHeld: true, IsCompleted: false } hold ? hold : null;

    /// <summary>The calling flow's turn to change roles, ended by disposing what returns; a hold's lasts until the hold ends.</summary>
    public async Task<IAsyncDisposable?> Enter(CancellationToken ct)
    {
        if (CurrentHold is { } hold)
        {
            await EnterHold(hold, ct).ConfigureAwait(false);
            return null;
        }

        await _turnstile.WaitAsync(ct).ConfigureAwait(false);
        lock (_sync) _underWay++;
        _turnstile.Release();
        return new Exit(this);
    }

    /// <summary>
    /// Runs <paramref name="invalidate"/> now, or, inside a hold, when it commits: a rollback leaves the compiled state as
    /// it is, and every invalidation of a hold runs before any of its after-events.
    /// </summary>
    public void Invalidate(Action invalidate)
    {
        if (CurrentHold is { } hold && _holds.TryGetValue(hold, out var changes)) changes.Invalidate(invalidate);
        else invalidate();
    }

    /// <summary>
    /// Raises an after-event now, or, inside a hold, once it has committed, with a token of that moment rather than the
    /// change's, and with a failure logged by the coordinator because the commit stands.
    /// </summary>
    public static ValueTask Published<TContext>(RoleEventKind kind, Func<CancellationToken, TContext> context, CancellationToken ct)
    {
        if (CurrentHold is not { } hold) return RoleEventRegistry.After(kind, context(ct));
        hold.AfterCommit(token => RoleEventRegistry.After(kind, context(token)).AsTask());
        return ValueTask.CompletedTask;
    }

    private async Task EnterHold(ITransactionCoordinator hold, CancellationToken ct)
    {
        var changes = _holds.GetValue(hold, static _ => new HeldChanges());
        await changes.Entering.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (changes.Entered) return;
            var began = Stopwatch.GetTimestamp();
            if (!await _turnstile.WaitAsync(heldChangeWait, ct).ConfigureAwait(false)) throw Refused(hold);
            try
            {
                await Drained(heldChangeWait - Stopwatch.GetElapsedTime(began), ct).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                _turnstile.Release();
                if (failure is TimeoutException) throw Refused(hold);
                throw;
            }

            hold.WhenHoldEnds(() => _turnstile.Release());
            hold.AfterCommit(changes.RunInvalidations);
            changes.Entered = true;
        }
        finally
        {
            changes.Entering.Release();
        }
    }

    /// <summary>Waits for the changes under way to end; none can begin while the turnstile is held.</summary>
    private Task Drained(TimeSpan bound, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_underWay == 0) return Task.CompletedTask;
            _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _drained.Task.WaitAsync(bound > TimeSpan.Zero ? bound : TimeSpan.Zero, ct);
        }
    }

    private void Left()
    {
        lock (_sync)
        {
            if (--_underWay == 0) _drained?.TrySetResult();
        }
    }

    private TimeoutException Refused(ITransactionCoordinator hold) => new(
        $"A role change inside hold '{hold.Name}' waited {heldChangeWait} for another role change to finish, which may need " +
        $"the store this hold keeps ({RoleOptions.SectionPath}:{nameof(RoleOptions.HeldChangeWait)}). Nothing of this change " +
        "was written. End the scope, which frees the store for that change, and start again.");

    private sealed class Exit(RoleChangeGate gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Left();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>What one hold has done here: whether it has passed the gate, and what its commit invalidates.</summary>
    private sealed class HeldChanges
    {
        private readonly List<Action> _invalidations = [];

        public SemaphoreSlim Entering { get; } = new(1, 1);
        public bool Entered { get; set; }

        public void Invalidate(Action invalidate)
        {
            lock (_invalidations) _invalidations.Add(invalidate);
        }

        public Task RunInvalidations(CancellationToken ct)
        {
            lock (_invalidations)
            {
                foreach (var invalidate in _invalidations) invalidate();
            }

            return Task.CompletedTask;
        }
    }
}
