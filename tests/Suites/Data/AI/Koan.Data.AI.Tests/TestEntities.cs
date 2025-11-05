using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Test entity with AllStrings policy (default)
/// </summary>
[Storage(Name = "TestDocs")]
[Embedding] // Uses AllStrings policy by default
public class TestDocument : Entity<TestDocument>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();

    [EmbeddingIgnore]
    public string InternalId { get; set; } = "";

    public int ViewCount { get; set; }
}

/// <summary>
/// Test entity with explicit properties
/// </summary>
[Storage(Name = "TestArticles")]
[Embedding(Properties = new[] { nameof(Title), nameof(Summary) })]
public class TestArticle : Entity<TestArticle>
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string InternalNotes { get; set; } = ""; // Not included in embedding
}

/// <summary>
/// Test entity with template
/// </summary>
[Storage(Name = "TestPosts")]
[Embedding(Template = "Title: {Title}\nAuthor: {Author}\nContent: {Content}")]
public class TestPost : Entity<TestPost>
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// Test entity with async embedding
/// </summary>
[Storage(Name = "TestAsyncDocs")]
[Embedding(Async = true, RateLimitPerMinute = 30)]
public class TestAsyncDocument : Entity<TestAsyncDocument>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// Test entity with explicit policy
/// </summary>
[Storage(Name = "TestExplicit")]
[Embedding(Policy = EmbeddingPolicy.Explicit, Properties = new[] { nameof(PublicField) })]
public class TestExplicitEntity : Entity<TestExplicitEntity>
{
    public string PublicField { get; set; } = "";
    public string PrivateField { get; set; } = "";
}
