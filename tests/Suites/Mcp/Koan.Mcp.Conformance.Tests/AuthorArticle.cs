using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Mcp;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN7 (docs/assessment/09 §5) — a parent entity for the governed edge-traversal specs. Public (no scopes).
/// <see cref="Article"/> declares TWO edges back to it (AuthorId / EditorId) — same target, different field —
/// so the catalog projects both navigable edges (the works-authored / works-reviewed shape).
/// </summary>
[McpEntity(Name = "author", Description = "A content author", Exposure = McpExposureMode.Full)]
[StorageName("an7_authors")]
public sealed class Author : Entity<Author>
{
    public string Name { get; set; } = "";
}

/// <summary>A SCOPED child entity (requires <c>articles:read</c>) — so an unscoped remote grant walls it, and
/// the author's edges to it must then be ABSENT from the catalog (walled-means-silent at the edge level).</summary>
[McpEntity(Name = "article", Description = "An article", Exposure = McpExposureMode.Full, RequiredScopes = new[] { "articles:read" })]
[StorageName("an7_articles")]
public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";

    [Parent(typeof(Author))]
    public string? AuthorId { get; set; }

    [Parent(typeof(Author))]
    public string? EditorId { get; set; }
}

[Route("api/authors")]
public sealed class AuthorsController : EntityController<Author>
{
}

[Route("api/articles")]
public sealed class ArticlesController : EntityController<Article>
{
}
