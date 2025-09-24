using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S10.DevPortal.Models;

namespace S10.DevPortal.Controllers;

/// <summary>
/// Demonstrates threaded comments with parent/child navigation
/// </summary>
[Route("api/[controller]")]
public class CommentsController : EntityController<Comment>
{
    // Inherits all CRUD operations automatically

    /// <summary>
    /// Demonstrates threaded comment structure for an article
    /// </summary>
    [HttpGet("thread/{articleId}")]
    public async Task<IActionResult> GetCommentThread(string articleId)
    {
        var comments = await Comment.Query($"ArticleId == '{articleId}'");
        var commentTree = BuildCommentTree(comments);
        return Ok(commentTree);
    }

    /// <summary>
    /// Demonstrates relationship navigation for comment threading
    /// </summary>
    [HttpGet("{id}/replies")]
    public async Task<IActionResult> GetReplies(string id)
    {
        var comment = await Comment.Get(id);
        if (comment == null) return NotFound();

        var replies = await comment.GetChildren<Comment>();
        return Ok(replies);
    }

    /// <summary>
    /// Builds threaded comment structure from flat list
    /// </summary>
    private static object BuildCommentTree(IEnumerable<Comment> comments)
    {
        var commentList = comments.ToList();
        var topLevel = commentList.Where(c => string.IsNullOrEmpty(c.ParentCommentId)).ToList();

        return topLevel.Select(comment => new
        {
            Comment = comment,
            Replies = GetRepliesRecursive(comment.Id, commentList)
        });
    }

    /// <summary>
    /// Recursively builds reply structure
    /// </summary>
    private static IEnumerable<object> GetRepliesRecursive(string parentId, List<Comment> allComments)
    {
        var directReplies = allComments.Where(c => c.ParentCommentId == parentId).ToList();

        return directReplies.Select(reply => new
        {
            Comment = reply,
            Replies = GetRepliesRecursive(reply.Id, allComments)
        });
    }
}