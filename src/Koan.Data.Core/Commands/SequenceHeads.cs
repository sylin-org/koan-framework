using System.Collections.Concurrent;

namespace Koan.Data.Core.Commands;

/// <summary>
/// The committed head of each <see cref="Sequence"/> this host has asked or raised (DATA-0169), one per host beside the transaction
/// factory, so two hosts in one process keep two. A head only ever rises, and only to a number that has committed with every number below it:
/// a store read outside a hold, or the last number a hold took once its native commit has landed.
/// </summary>
internal sealed class SequenceHeads
{
    private readonly ConcurrentDictionary<Sequence, Head> _heads = new();

    internal Head Of(Sequence sequence) => _heads.GetOrAdd(sequence, static _ => new Head());

    /// <summary>What a spec runs in a wait between its read of the head and its wait, to land a commit there.</summary>
    internal Func<Task>? BetweenReadAndWait { get; set; }

    /// <summary>One sequence's head, and the wake a raise completes.</summary>
    internal sealed class Head
    {
        private long _value;
        private volatile bool _read;
        private TaskCompletionSource _wake = NewWake();

        /// <summary>The head as this host knows it, which lags what has committed and never leads it.</summary>
        internal long Value => Interlocked.Read(ref _value);

        /// <summary>Whether the store has been read once, as the first ask does.</summary>
        internal bool Read => _read;

        /// <summary>Completes at the next raise that moves the head, so a waiter takes it before it reads <see cref="Value"/>.</summary>
        internal Task Wake => Volatile.Read(ref _wake).Task;

        /// <summary>A compare-and-swap maximum, so the order raises arrive in does not matter. A raise that moves the head wakes every waiter.</summary>
        internal void Raise(long to)
        {
            var seen = Interlocked.Read(ref _value);
            while (to > seen)
            {
                var was = Interlocked.CompareExchange(ref _value, to, seen);
                if (was == seen)
                {
                    Interlocked.Exchange(ref _wake, NewWake()).TrySetResult();
                    return;
                }
                seen = was;
            }
        }

        /// <summary>Raises the head to the top a store read found.</summary>
        internal void Learn(long top)
        {
            Raise(top);
            _read = true;
        }

        // A raise runs inside a sender's commit, so a waiter's continuation never runs on that flow.
        private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
