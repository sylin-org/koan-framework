using AwesomeAssertions;
using Xunit;

namespace Koan.AI.Prompt.Tests;

public record TestResponse(string Answer, double Confidence);

public class PromptTests
{
    [Fact]
    public void Parse_ExtractsVariables()
    {
        var prompt = Prompt.Parse("Hello {name}, welcome to {place}");

        prompt.Variables.Should().BeEquivalentTo(["name", "place"]);
    }

    [Fact]
    public void Parse_NoVariables()
    {
        var prompt = Prompt.Parse("Hello world");

        prompt.Variables.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DuplicateVariables()
    {
        var prompt = Prompt.Parse("{x} and {x}");

        prompt.Variables.Should().BeEquivalentTo(["x"]);
    }

    [Fact]
    public void ImplicitConversion_FromString()
    {
        Prompt p = "hello {name}";

        p.Raw.Should().Be("hello {name}");
    }

    [Fact]
    public void ImplicitConversion_ToString()
    {
        Prompt p = Prompt.Parse("hello {name}");

        string s = p;

        s.Should().Be("hello {name}");
    }

    [Fact]
    public void Resolve_WithAnonymousObject()
    {
        var prompt = Prompt.Parse("Hello {name}, welcome to {place}");

        var result = prompt.Resolve(new { name = "Alex", place = "Prism" });

        result.Should().Be("Hello Alex, welcome to Prism");
    }

    [Fact]
    public void Resolve_WithDictionary()
    {
        var prompt = Prompt.Parse("Hello {name}, welcome to {place}");

        var result = prompt.Resolve(new Dictionary<string, string>
        {
            ["name"] = "Alex",
            ["place"] = "Prism"
        });

        result.Should().Be("Hello Alex, welcome to Prism");
    }

    [Fact]
    public void Resolve_MissingVariable_KeepsPlaceholder()
    {
        var prompt = Prompt.Parse("Hello {name}, welcome to {place}");

        var result = prompt.Resolve(new { name = "Alex" });

        result.Should().Be("Hello Alex, welcome to {place}");
    }

    [Fact]
    public void Resolve_WithDefaults()
    {
        var prompt = Prompt.Create(p => p
            .Instruct("Hello {name}, welcome to {place}")
            .Default("place", "Earth"));

        var result = prompt.Resolve(new { name = "Alex" });

        result.Should().Be("Hello Alex, welcome to Earth");
    }

    [Fact]
    public void Resolve_ContextOverridesDefault()
    {
        var prompt = Prompt.Create(p => p
            .Instruct("Hello {name}, welcome to {place}")
            .Default("place", "Earth"));

        var result = prompt.Resolve(new { name = "Alex", place = "Mars" });

        result.Should().Be("Hello Alex, welcome to Mars");
    }

    [Fact]
    public void UnresolvedVariables_ReturnsCorrectly()
    {
        var prompt = Prompt.Parse("Hello {name}, welcome to {place}");

        var unresolved = prompt.UnresolvedVariables(new { name = "Alex" });

        unresolved.Should().BeEquivalentTo(["place"]);
    }

    [Fact]
    public void Builder_SystemAndInstruct()
    {
        var prompt = Prompt.Create(p => p
            .System("You are X")
            .Instruct("Do {thing}"));

        prompt.System.Should().Be("You are X");
        prompt.Template.Should().Be("Do {thing}");
    }

    [Fact]
    public void Builder_Constraints()
    {
        var prompt = Prompt.Create(p => p
            .Instruct("Do something")
            .Constrain("Be concise", "Max 3 sentences"));

        prompt.Constraints.Should().HaveCount(2);
        prompt.Raw.Should().Contain("Be concise");
        prompt.Raw.Should().Contain("Max 3 sentences");
    }

    [Fact]
    public void Builder_OutputAs_GeneratesSchema()
    {
        var prompt = Prompt.Create(p => p
            .Instruct("Generate response")
            .OutputAs<TestResponse>());

        prompt.OutputFormat.Should().NotBeNull();
        prompt.OutputFormat!.ExpectedFields.Should().Contain("Answer");
        prompt.OutputFormat.ExpectedFields.Should().Contain("Confidence");
    }

    [Fact]
    public void Builder_Examples()
    {
        var prompt = Prompt.Create(p => p
            .Instruct("Translate")
            .Example("hello", "hola"));

        prompt.Examples.Should().HaveCount(1);
    }

    [Fact]
    public void Builder_Meta()
    {
        var prompt = Prompt.Create(p => p
            .Instruct("Do something")
            .Meta("author", "test"));

        prompt.Meta["author"].Should().Be("test");
    }

    [Fact]
    public void With_ReturnsNewPrompt()
    {
        var original = Prompt.Create(p => p
            .System("Original")
            .Instruct("Do {thing}"));

        var modified = original.With(p => p.System("New"));

        modified.Should().NotBeSameAs(original);
        modified.System.Should().Be("New");
    }

    [Fact]
    public void With_OriginalUnchanged()
    {
        var original = Prompt.Create(p => p
            .System("Original")
            .Instruct("Do {thing}"));

        _ = original.With(p => p.System("New"));

        original.System.Should().Be("Original");
    }
}
