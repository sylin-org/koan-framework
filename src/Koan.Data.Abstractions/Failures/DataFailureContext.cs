using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Abstractions.Failures;

/// <summary>Safe boundary facts supplied to an adapter's native-failure translator.</summary>
public sealed record DataFailureContext(
    string Provider,
    string Source,
    string Operation,
    DataOperationEffect Effect,
    bool Dispatched,
    bool CommitBoundaryCrossed);
