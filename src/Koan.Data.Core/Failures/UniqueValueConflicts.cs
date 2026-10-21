using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.Failures;

/// <summary>
/// Reports a store's unique-index violation as a <see cref="UniqueValueConflictException"/> (DATA-0150). The adapter
/// says what its error means through its <see cref="IDataFailureClassifier"/> (code
/// <see cref="UniqueValueConflictException.FailureCode"/>); the repository facade decides how it is reported, once for
/// every write verb, and withholds the store's error, which names the index that refused the value.
/// </summary>
internal static class UniqueValueConflicts
{
    public static bool TryTranslate(
        Exception error,
        Type entity,
        string provider,
        DataSourcePlan source,
        out UniqueValueConflictException conflict)
    {
        conflict = null!;
        if (error is UniqueValueConflictException or OperationCanceledException) return false;
        if (AppHost.Current?.GetService(typeof(IEnumerable<IDataFailureClassifier>)) is not IEnumerable<IDataFailureClassifier> classifiers)
            return false;
        var context = new DataFailureContext(provider, source.Source, "entity write", DataOperationEffect.Write,
            Dispatched: true, CommitBoundaryCrossed: false);
        if (!DataFailurePolicy.TryClassify(error, context, classifiers, out var failure) ||
            !string.Equals(failure.Code, UniqueValueConflictException.FailureCode, StringComparison.Ordinal))
            return false;
        conflict = new UniqueValueConflictException(entity.Name, failure);
        return true;
    }
}
