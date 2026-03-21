using FluentAssertions;
using Xunit;

namespace Koan.AI.Integration.Tests;

public sealed class PromptIntegrationTests
{
    [Fact]
    public void Prompt_ParseResolveRoundTrip()
    {
        var prompt = Koan.AI.Prompt.Prompt.Parse("Summarize {topic} for {audience}");

        prompt.Variables.Should().Contain("topic").And.Contain("audience");
        prompt.Raw.Should().Be("Summarize {topic} for {audience}");

        var resolved = prompt.Resolve(new { topic = "AI safety", audience = "engineers" });

        resolved.Should().Be("Summarize AI safety for engineers");
    }

    [Fact]
    public void Prompt_BuilderWithOutputSpec_ProducesSchema()
    {
        var prompt = Koan.AI.Prompt.Prompt.Create(p => p
            .System("You are a classification engine")
            .Instruct("Classify this text: {text}")
            .OutputAs<TestClassificationResponse>());

        prompt.System.Should().Be("You are a classification engine");
        prompt.Variables.Should().Contain("text");
        prompt.OutputFormat.Should().NotBeNull();
        prompt.OutputFormat!.JsonSchema.Should().NotBeNullOrEmpty();
        prompt.OutputFormat.ExpectedFields.Should().Contain("Category");
        prompt.OutputFormat.ExpectedFields.Should().Contain("Confidence");
    }

    [Fact]
    public void Prompt_ImplicitConversion_WorksWithStringApis()
    {
        Koan.AI.Prompt.Prompt prompt = "hello {name}";

        prompt.Raw.Should().Be("hello {name}");
        prompt.Variables.Should().ContainSingle().Which.Should().Be("name");

        // Implicit conversion to string
        string text = prompt;
        text.Should().Be("hello {name}");
    }

    [Fact]
    public void Prompt_UnresolvedVariables_DetectsMissing()
    {
        var prompt = Koan.AI.Prompt.Prompt.Parse("Hello {first} {last}");

        var unresolved = prompt.UnresolvedVariables(new { first = "Alice" });

        unresolved.Should().ContainSingle().Which.Should().Be("last");
    }

    [Fact]
    public void Prompt_WithDefaults_FillsMissingVariables()
    {
        var prompt = Koan.AI.Prompt.Prompt.Create(p => p
            .Instruct("Hello {name}, your role is {role}")
            .Default("role", "user"));

        var resolved = prompt.Resolve(new { name = "Alice" });

        resolved.Should().Contain("Alice");
        resolved.Should().Contain("user");
    }

    [Fact]
    public void Prompt_WithConstraints_IncludesInOutput()
    {
        var prompt = Koan.AI.Prompt.Prompt.Create(p => p
            .Instruct("Summarize {content}")
            .Constrain("Be concise", "Max 3 sentences"));

        prompt.Constraints.Should().HaveCount(2);
        prompt.Raw.Should().Contain("Be concise");
        prompt.Raw.Should().Contain("Max 3 sentences");
    }

    [Fact]
    public void Prompt_ImmutableModification_PreservesOriginal()
    {
        var original = Koan.AI.Prompt.Prompt.Parse("Hello {name}");
        var modified = original.With(p => p.System("You are helpful"));

        original.System.Should().BeNull("original should not be modified");
        modified.System.Should().Be("You are helpful");
        modified.Variables.Should().Contain("name");
    }

    // Test response type for OutputAs<T>()
    private sealed class TestClassificationResponse
    {
        public string Category { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }
}
