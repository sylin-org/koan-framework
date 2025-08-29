namespace S7.ContentPlatform.Models;

/// <summary>
/// Article status in the publication workflow.
/// </summary>
public enum ArticleStatus
{
    /// <summary>
    /// Article is being written, not ready for review.
    /// </summary>
    Draft = 0,
    
    /// <summary>
    /// Article submitted for editorial review.
    /// </summary>
    UnderReview = 1,
    
    /// <summary>
    /// Article approved and published.
    /// </summary>
    Published = 2,
    
    /// <summary>
    /// Article rejected by editor, needs revision.
    /// </summary>
    Rejected = 3,
    
    /// <summary>
    /// Published article that has been archived/unpublished.
    /// </summary>
    Archived = 4
}