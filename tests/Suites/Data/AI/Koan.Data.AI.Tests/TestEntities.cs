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

/// <summary>
/// Test entity with template containing non-existent property
/// </summary>
[Storage(Name = "TestTemplateBad")]
[Embedding(Template = "Title: {Title}\nMissing: {NonExistentProp}\nContent: {Content}")]
public class TestTemplateWithBadProperty : Entity<TestTemplateWithBadProperty>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    // NonExistentProp does not exist
}

/// <summary>
/// Test entity with template containing mix of valid and invalid properties
/// </summary>
[Storage(Name = "TestTemplateMixed")]
[Embedding(Template = "{ValidProp} - {InvalidProp} - {AnotherValid}")]
public class TestTemplateMixedProperties : Entity<TestTemplateMixedProperties>
{
    public string ValidProp { get; set; } = "";
    public string AnotherValid { get; set; } = "";
    // InvalidProp does not exist
}

/// <summary>
/// Test entity with template and nullable property
/// </summary>
[Storage(Name = "TestTemplateNullable")]
[Embedding(Template = "Required: {RequiredField}, Optional: {OptionalField}")]
public class TestTemplateWithNullable : Entity<TestTemplateWithNullable>
{
    public string RequiredField { get; set; } = "";
    public string? OptionalField { get; set; }
}

/// <summary>
/// Test entity with AllPublic policy
/// </summary>
[Storage(Name = "TestAllPublic")]
[Embedding(Policy = EmbeddingPolicy.AllPublic)]
public class TestAllPublicEntity : Entity<TestAllPublicEntity>
{
    public string StringProp { get; set; } = "";
    public int IntProp { get; set; }
    public bool BoolProp { get; set; }

    [EmbeddingIgnore]
    public string IgnoredProp { get; set; } = "";
}

/// <summary>
/// Test entity with empty properties array
/// </summary>
[Storage(Name = "TestEmptyProps")]
[Embedding(Properties = new string[0])]
public class TestEmptyPropertiesArray : Entity<TestEmptyPropertiesArray>
{
    public string Field1 { get; set; } = "";
    public string Field2 { get; set; } = "";
}
