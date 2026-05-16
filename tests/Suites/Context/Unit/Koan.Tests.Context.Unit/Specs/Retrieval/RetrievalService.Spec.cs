using FluentAssertions;
using Koan.Context.Models;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Retrieval;

/// <summary>
/// Validates the normalized search request context builder.
/// </summary>
public class SearchRequestContext_Spec
{
    [Fact]
    public void Create_NormalizesTagsAndProjects()
    {
        var context = SearchRequestContext.Create(
            query: " docs  roadmap ",
            projectIds: new[]
            {
                "019a6584-3075-7076-ae69-4ced4e2799f5",
                " 019a6584-3075-7076-ae69-4ced4e2799f5 ",
                " 7db25a9f-2f59-4b7c-a92e-2e2a8a6e8d3e "
            },
            tagsAny: new[] { " Docs ", "ARCH" },
            tagsAll: new[] { " architecture " },
            tagsExclude: new[] { "drafts" });

        context.ProjectIds.Should().HaveCount(2).And.Contain(new[]
        {
            "019a6584-3075-7076-ae69-4ced4e2799f5",
            "7db25a9f-2f59-4b7c-a92e-2e2a8a6e8d3e"
        });
        context.TagsAny.Should().BeEquivalentTo(new[] { "docs", "arch" });
        context.TagsAll.Should().BeEquivalentTo(new[] { "architecture" });
        context.TagsExclude.Should().BeEquivalentTo(new[] { "drafts" });
    }

    [Fact]
    public void Create_AppliesMcpDefaults()
    {
        var context = SearchRequestContext.Create(
            query: "search",
            channel: SearchChannel.Mcp);

        context.MaxTokens.Should().Be(6000);
        context.IncludeInsights.Should().BeFalse();
        context.IncludeReasoning.Should().BeFalse();
    }

    [Fact]
    public void Create_ClampsTokenBudget()
    {
        var context = SearchRequestContext.Create(
            query: "search",
            maxTokens: 50);

        context.MaxTokens.Should().Be(1000);

        context = SearchRequestContext.Create(
            query: "search",
            maxTokens: 50000);

        context.MaxTokens.Should().Be(20000);
    }
}
