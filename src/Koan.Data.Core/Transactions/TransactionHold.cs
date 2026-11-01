using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// What <see cref="EntityContext.Hold"/> returns: awaiting it waits for the native transaction and yields the scope. The
/// scope enters the ambient context as the await completes, in the caller's own flow, so a hold that could not begin leaves
/// nothing behind in that flow.
/// </summary>
public readonly struct TransactionHold
{
    private readonly Task _begun;
    private readonly Func<IDisposable> _enter;

    internal TransactionHold(Task begun, Func<IDisposable> enter)
    {
        _begun = begun;
        _enter = enter;
    }

    public Awaiter GetAwaiter() => new(_begun.GetAwaiter(), _enter);

    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        private readonly TaskAwaiter _inner;
        private readonly Func<IDisposable> _enter;

        internal Awaiter(TaskAwaiter inner, Func<IDisposable> enter)
        {
            _inner = inner;
            _enter = enter;
        }

        public bool IsCompleted => _inner.IsCompleted;

        public IDisposable GetResult()
        {
            _inner.GetResult();
            return _enter();
        }

        public void OnCompleted(Action continuation) => _inner.OnCompleted(continuation);

        public void UnsafeOnCompleted(Action continuation) => _inner.UnsafeOnCompleted(continuation);
    }
}
