using AwesomeAssertions;
using Koan.AI.Pipeline;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Routing;

/// <summary>
/// Tests for AiRecipeProvider: configuration-driven capability-to-model bindings.
/// </summary>
[Trait("ADR", "AI-0032")]
[Trait("Category", "Unit")]
public sealed class AiRecipeProviderSpec
{
    [Fact]
    public void No_active_recipe_returns_null_for_all_categories()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var provider = new AiRecipeProvider(config);

        provider.ActiveRecipeName.Should().BeNull();
        provider.GetModel("Chat").Should().BeNull();
        provider.GetModel("Embed").Should().BeNull();
        provider.GetModel("Ocr").Should().BeNull();
    }

    [Fact]
    public void Active_recipe_with_bindings_returns_model_for_matching_category()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Ai:ActiveRecipe"] = "fast",
                ["Koan:Ai:Recipes:fast:Chat"] = "llama3",
                ["Koan:Ai:Recipes:fast:Embed"] = "nomic",
            })
            .Build();

        var provider = new AiRecipeProvider(config);

        provider.ActiveRecipeName.Should().Be("fast");
        provider.GetModel("Chat").Should().Be("llama3");
        provider.GetModel("Embed").Should().Be("nomic");
    }

    [Fact]
    public void Sparse_recipe_returns_null_for_unbound_category()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Ai:ActiveRecipe"] = "chat-only",
                ["Koan:Ai:Recipes:chat-only:Chat"] = "llama3",
            })
            .Build();

        var provider = new AiRecipeProvider(config);

        provider.GetModel("Chat").Should().Be("llama3");
        provider.GetModel("Embed").Should().BeNull("sparse recipe has no opinion on Embed");
    }

    [Fact]
    public void Missing_recipe_section_logs_warning_and_returns_null()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Ai:ActiveRecipe"] = "nonexistent",
            })
            .Build();

        var provider = new AiRecipeProvider(config);

        provider.ActiveRecipeName.Should().Be("nonexistent");
        provider.GetModel("Chat").Should().BeNull();
        provider.GetModel("Embed").Should().BeNull();
    }

    [Fact]
    public void Recipe_name_is_trimmed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Ai:ActiveRecipe"] = " fast ",
                ["Koan:Ai:Recipes:fast:Chat"] = "llama3",
            })
            .Build();

        var provider = new AiRecipeProvider(config);

        provider.ActiveRecipeName.Should().Be("fast");
        provider.GetModel("Chat").Should().Be("llama3");
    }

    [Fact]
    public void GetModel_is_case_insensitive()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Ai:ActiveRecipe"] = "mixed",
                ["Koan:Ai:Recipes:mixed:chat"] = "llama3",
            })
            .Build();

        var provider = new AiRecipeProvider(config);

        provider.GetModel("Chat").Should().Be("llama3");
        provider.GetModel("CHAT").Should().Be("llama3");
        provider.GetModel("chat").Should().Be("llama3");
    }
}
