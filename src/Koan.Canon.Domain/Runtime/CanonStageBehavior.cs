namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Controls how canonization interacts with staging.
/// </summary>
public enum CanonStageBehavior
{
    /// <summary>
    /// Uses the engine defaults (stage when required by pipeline configuration).
    /// </summary>
    Default = 0,

    /// <summary>
    /// Force staging without immediate processing.
    /// </summary>
    StageOnly = 1,

    /// <summary>
    /// Bypass staging and process immediately.
    /// </summary>
    Immediate = 2
}
