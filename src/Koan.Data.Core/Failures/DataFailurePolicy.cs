using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Core.Failures;

/// <summary>
/// The consumer half of <see cref="IDataFailureClassifier"/>: asks the registered classifiers what a native
/// failure actually was, so a caller can decide whether its own operation may be attempted again.
///
/// <para>The seam existed with no producers and no consumers, which is why a SQL Server deadlock — the one
/// failure whose documented remedy is "rerun the transaction" — propagated raw out of a job drain. The split it
/// draws is the one DATA-0119 draws everywhere else: the adapter knows what its store's error number means, and
/// the framework decides what that meaning earns. Neither half can be written by the other.</para>
///
/// <para>Nothing here retries anything. A disposition of <see cref="DataRetryDisposition.RequiresIdempotency"/>
/// is a statement about the store, not about the caller, and only the caller knows whether its operation can be
/// run twice. This answers the question; acting on the answer stays where the knowledge is.</para>
/// </summary>
public static class DataFailurePolicy
{
    /// <summary>
    /// The first classification any registered classifier will make of this failure, or <see langword="false"/>
    /// when none recognises it. An unrecognised failure stays exactly as raw as it was.
    /// </summary>
    public static bool TryClassify(
        Exception failure,
        DataFailureContext context,
        IEnumerable<IDataFailureClassifier>? classifiers,
        out DataFailure classified)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(context);

        if (classifiers is not null)
        {
            foreach (var classifier in classifiers)
            {
                if (classifier.TryClassify(failure, context, out var candidate))
                {
                    classified = candidate;
                    return true;
                }
            }
        }

        classified = default!;
        return false;
    }

    /// <summary>
    /// Whether an operation the caller knows to be idempotent may be attempted again after this failure.
    ///
    /// <para>Two things have to hold, and they are separate questions. The store must say nothing was committed —
    /// a failure that may have landed is never safe to repeat, whatever its retry disposition claims — and the
    /// adapter must permit a retry at all. <see cref="DataRetryDisposition.BeforeDispatchOnly"/> permits one
    /// only where the operation never reached the store, so it is honoured strictly rather than folded in with
    /// the looser case.</para>
    ///
    /// <para>The caller asserts the idempotency and nothing here can check it. Say yes only where a repeat is
    /// harmless by construction — a conditional write that either wins or reports that someone else did.</para>
    /// </summary>
    public static bool MayRetryIdempotent(
        Exception failure,
        DataFailureContext context,
        IEnumerable<IDataFailureClassifier>? classifiers)
    {
        if (!TryClassify(failure, context, classifiers, out var classified)) return false;

        return classified.Retry switch
        {
            DataRetryDisposition.RequiresIdempotency =>
                classified.CommitOutcome is DataCommitOutcome.NotDispatched or DataCommitOutcome.NotCommitted,
            DataRetryDisposition.BeforeDispatchOnly =>
                classified.CommitOutcome is DataCommitOutcome.NotDispatched,
            _ => false
        };
    }
}
