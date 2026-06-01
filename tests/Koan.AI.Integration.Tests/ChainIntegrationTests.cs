using FluentAssertions;
using Koan.AI.Orchestration;
using Xunit;

namespace Koan.AI.Integration.Tests;

/// <summary>
/// Integration tests for the Chain builder and definition pipeline.
/// Tests the immutable builder API, step composition, and variable resolution
/// at the definition level. Full execution tests (which require DI-hosted ChainExecutor
/// and a live AppHost) belong in the E2E test suite.
/// </summary>
public sealed class ChainIntegrationTests
{
    [Fact]
    public void Create_ReturnsNewBuilder()
    {
        var builder = Chain.Create();

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Chat_AddsStepToDefinition()
    {
        var definition = Chain.Create()
            .Chat("Hello {name}")
            .Build();

        definition.Steps.Should().ContainSingle();
        definition.Steps[0].Kind.Should().Be(ChainStepKind.Chat);
        definition.Steps[0].Value.Should().Be("Hello {name}");
    }

    [Fact]
    public void System_SetsSystemMessage()
    {
        var definition = Chain.Create()
            .System("You are a helpful assistant")
            .Chat("test")
            .Build();

        definition.SystemMessage.Should().Be("You are a helpful assistant");
    }

    [Fact]
    public void Parse_AddsParseStep()
    {
        var definition = Chain.Create()
            .Chat("return json")
            .Parse<TestResponse>()
            .Build();

        definition.Steps.Should().HaveCount(2);
        definition.Steps[1].Kind.Should().Be(ChainStepKind.Parse);
        definition.Steps[1].EntityType.Should().Be(typeof(TestResponse));
    }

    [Fact]
    public void Scope_SetsModelTargets()
    {
        var definition = Chain.Create()
            .Scope(chat: "ollama", embed: "onnx")
            .Chat("test")
            .Build();

        definition.ChatModel.Should().Be("ollama");
        definition.EmbedModel.Should().Be("onnx");
    }

    [Fact]
    public void Classify_AddsStepWithCategories()
    {
        var categories = new[] { "positive", "negative", "neutral" };

        var definition = Chain.Create()
            .Classify("Some text", categories)
            .Build();

        definition.Steps.Should().ContainSingle();
        definition.Steps[0].Kind.Should().Be(ChainStepKind.Classify);
        definition.Steps[0].Categories.Should().BeEquivalentTo(categories);
    }

    [Fact]
    public void ImmutableBuilder_OriginalNotMutated()
    {
        var original = Chain.Create().System("System A");
        var modified = original.Chat("Hello");

        var originalDef = original.Build();
        var modifiedDef = modified.Build();

        originalDef.Steps.Should().BeEmpty("original builder should have no steps");
        modifiedDef.Steps.Should().ContainSingle("modified builder should have one step");
        originalDef.SystemMessage.Should().Be("System A");
        modifiedDef.SystemMessage.Should().Be("System A");
    }

    [Fact]
    public void MultipleSteps_PreservesOrder()
    {
        var definition = Chain.Create()
            .System("System")
            .Chat("Step 1")
            .Chat("Step 2")
            .Chat("Step 3")
            .Build();

        definition.Steps.Should().HaveCount(3);
        definition.Steps[0].Value.Should().Be("Step 1");
        definition.Steps[1].Value.Should().Be("Step 2");
        definition.Steps[2].Value.Should().Be("Step 3");
    }

    [Fact]
    public void Retrieve_SetsTopKAndAlpha()
    {
        var definition = Chain.Create()
            .Retrieve<TestResponse>("search query", topK: 10, alpha: 0.7, rerank: true)
            .Build();

        var step = definition.Steps.Should().ContainSingle().Subject;
        step.Kind.Should().Be(ChainStepKind.Retrieve);
        step.TopK.Should().Be(10);
        step.Alpha.Should().Be(0.7);
        step.Rerank.Should().BeTrue();
        step.EntityType.Should().Be(typeof(TestResponse));
    }

    [Fact] // AI-0036 P1-AI PHIL-1: no filter arg => no filter (today's behaviour, unchanged)
    public void Retrieve_WithoutFilter_HasNullFilter()
    {
        var step = Chain.Create().Retrieve<TestResponse>("q").Build().Steps.Single();
        step.Filter.Should().BeNull();
    }

    [Fact] // AI-0036 P1-AI: a lambda filter compiles to the unified Filter AST (same path as Entity<T>.Query)
    public void Retrieve_WithLambdaFilter_CompilesToFilter()
    {
        var step = Chain.Create()
            .Retrieve<TestResponse>("q", filter: r => r.Answer == "x")
            .Build().Steps.Single();
        step.Filter.Should().NotBeNull();
        step.Filter.Should().BeOfType<Koan.Data.Abstractions.Filtering.FieldFilter>();
    }

    [Fact]
    public void WithTools_AddsToolsStep()
    {
        var tool = Tool.From<IDisposable>("Dispose");

        var definition = Chain.Create()
            .WithTools(tool)
            .Chat("What is 2+2?")
            .Build();

        definition.Steps.Should().HaveCount(2);
        definition.Steps[0].Kind.Should().Be(ChainStepKind.Tools);
        definition.Steps[0].Tools.Should().ContainSingle();
    }

    [Fact]
    public void WithMemory_AttachesMemory()
    {
        var memory = ChainMemory.Sliding(maxTurns: 10);

        var definition = Chain.Create()
            .WithMemory(memory)
            .Chat("test")
            .Build();

        definition.Memory.Should().NotBeNull();
    }

    [Fact]
    public void SystemFromPrompt_UsesPromptRaw()
    {
        var prompt = AI.Prompt.Prompt.Parse("You are a {role} assistant");

        var definition = Chain.Create()
            .System(prompt)
            .Chat("test")
            .Build();

        definition.SystemMessage.Should().Be("You are a {role} assistant");
    }

    // ── Internal types ──

    private sealed class TestResponse
    {
        public string Answer { get; set; } = "";
        public double Confidence { get; set; }
    }
}
