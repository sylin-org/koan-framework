namespace S7.ContentPlatform.Models;

/// <summary>
/// Author role in the content platform.
/// </summary>
public enum AuthorRole
{
    /// <summary>
    /// Can create and edit their own articles.
    /// </summary>
    Writer = 0,
    
    /// <summary>
    /// Can review and approve/reject articles from writers.
    /// </summary>
    Editor = 1,
    
    /// <summary>
    /// Full administrative access.
    /// </summary>
    Admin = 2
}