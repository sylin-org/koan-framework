using AwesomeAssertions;
using Xunit;

namespace Koan.AI.Prompt.Tests;

public class OutputSpecTests
{
    [Fact]
    public void FromType_ExtractsFields()
    {
        var spec = OutputSpec.FromType<TestResponse>();

        spec.ExpectedFields.Should().Contain("Answer");
        spec.ExpectedFields.Should().Contain("Confidence");
    }

    [Fact]
    public void FromType_GeneratesJsonSchema()
    {
        var spec = OutputSpec.FromType<TestResponse>();

        spec.JsonSchema.Should().NotBeNull();
        spec.JsonSchema.Should().Contain("answer");
    }

    [Fact]
    public void WithFields_SetsFieldNames()
    {
        var spec = OutputSpec.WithFields("a", "b");

        spec.ExpectedFields.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void ToInstructionText_WithSchema()
    {
        var spec = OutputSpec.FromType<TestResponse>();

        var text = spec.ToInstructionText();

        text.Should().Contain("JSON");
    }

    [Fact]
    public void ToInstructionText_WithFields()
    {
        var spec = OutputSpec.WithFields("name", "age");

        var text = spec.ToInstructionText();

        text.Should().Contain("name");
        text.Should().Contain("age");
    }
}
