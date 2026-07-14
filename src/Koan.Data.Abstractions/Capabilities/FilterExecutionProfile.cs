namespace Koan.Data.Abstractions.Capabilities;

/// <summary>
/// Cost posture attached to <see cref="DataCaps.Query.FilterExecution"/>. Operator support remains
/// described separately by <c>FilterSupport</c>; this profile prevents semantic correctness from
/// being mistaken for backend pushdown efficiency.
/// </summary>
public sealed record FilterExecutionProfile(
    FilterExecutionKind Kind,
    bool SupportsBoundedCandidates = false);
