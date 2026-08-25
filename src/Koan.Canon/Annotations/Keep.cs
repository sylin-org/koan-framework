namespace Koan.Canon;

/// <summary>
/// Reconcile strategies for canonical properties: when arrivals disagree, what wins?
/// Undeclared properties reconcile as <see cref="Latest"/>.
/// </summary>
public enum Keep
{
    /// <summary>
    /// Retains the first non-null value encountered across sources.
    /// </summary>
    First = 0,

    /// <summary>
    /// Chooses the most recent value based on arrival ordering.
    /// </summary>
    Latest = 1,

    /// <summary>
    /// Chooses the minimum value using natural ordering.
    /// </summary>
    Min = 2,

    /// <summary>
    /// Chooses the maximum value using natural ordering.
    /// </summary>
    Max = 3,

    /// <summary>
    /// Prefers contributions from authoritative sources (declare them via
    /// <see cref="ReconcileAttribute.Source"/> / <c>Sources</c>) while they contribute,
    /// falling back to newest-wins until then.
    /// </summary>
    From = 4
}
