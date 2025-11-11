using System.Collections.Generic;
using FluentAssertions;
using Koan.Context.Models;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Mcp;

/// <summary>
/// Focused coverage for MCP request context normalization. These tests guard the
/// defaults that the controller relies on when projecting inbound tool calls into
/// the shared search pipeline.
/// </summary>
public class GetReferencesContext_Spec
{
    [Fact]
    public void Create_ForMcpChannel_DisablesInsightsAndReasoningByDefault()
    {
        // Act
        var context = SearchRequestContext.Create(
            query: "vector stores",
            projectIds: new[] { "019a6584-3075-7076-ae69-4ced4e2799f5" },
            channel: SearchChannel.Mcp,
            maxTokens: null,
            includeInsights: null,
            includeReasoning: null);

        // Assert
        context.Channel.Should().Be(SearchChannel.Mcp);
        context.IncludeInsights.Should().BeFalse();
        context.IncludeReasoning.Should().BeFalse();
        context.MaxTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Create_NormalizesTagFiltersAndBoosts()
    {
        // Arrange
        var tagBoosts = new Dictionary<string, float>
        {
            ["Docs"] = 1.5f,
            ["releases "] = 2.0f
        };

        // Act
        var context = SearchRequestContext.Create(
            query: "release notes",
            tagsAny: new[] { "  Docs", "docs" },
            tagsAll: new[] { "engineering" },
            tagsExclude: new[] { "Private " },
            tagBoosts: tagBoosts,
            channel: SearchChannel.Mcp);

        // Assert
        context.TagsAny.Should().ContainSingle(tag => tag == "docs");
        context.TagsAll.Should().ContainSingle(tag => tag == "engineering");
        context.TagsExclude.Should().ContainSingle(tag => tag == "private");
        context.TagBoosts.Should().ContainKey("docs").WhoseValue.Should().Be(1.5f);
        context.TagBoosts.Should().ContainKey("releases").WhoseValue.Should().Be(2.0f);
    }
}
