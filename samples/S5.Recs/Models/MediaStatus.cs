namespace S5.Recs.Models;

/// <summary>
/// Universal media consumption status that applies across all media types.
/// </summary>
public enum MediaStatus
{
    /// <summary>
    /// User plans to consume this media (Plan to Watch / Plan to Read)
    /// </summary>
    PlanToConsume = 0,

    /// <summary>
    /// User is currently consuming this media (Watching / Reading)
    /// </summary>
    Consuming = 1,

    /// <summary>
    /// User has completed consuming this media
    /// </summary>
    Completed = 2,

    /// <summary>
    /// User has dropped this media before completion
    /// </summary>
    Dropped = 3,

    /// <summary>
    /// User has paused consumption temporarily (On Hold / Paused)
    /// </summary>
    OnHold = 4
}