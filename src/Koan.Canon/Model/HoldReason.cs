namespace Koan.Canon;

/// <summary>
/// Why a canonization receipt is held, named by <em>who stopped the record</em> (segment grammar:
/// past participles — the engine stalled on it, a rule refused it).
/// </summary>
public enum HoldReason
{
    /// <summary>Mechanical: the funnel could not proceed (missing match key, failed match, failed verification).</summary>
    Stalled = 0,

    /// <summary>Business: a registered rule or a step deliberately said no via <c>ctx.Hold(why)</c>.</summary>
    Refused = 1,
}
