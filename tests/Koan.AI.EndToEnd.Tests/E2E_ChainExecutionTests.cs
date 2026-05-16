using FluentAssertions;
using Koan.AI.Contracts.Models;
using Koan.AI.Orchestration;
using Koan.Core.AI;
using Xunit;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// End-to-end tests verifying Chain.Create().Run() executes through the full
/// DI-bootstrapped framework: facade -> IChainExecutor -> Client -> IAiPipeline.
/// </summary>
[Trait("Category", "EndToEnd")]
[Collection("EndToEnd")]
public sealed class E2E_ChainExecutionTests : IDisposable
{
    private readonly KoanTestFixture _fixture;

    public E2E_ChainExecutionTests()
    {
        _fixture = new KoanTestFixture();
        _fixture.RegisterAdapter("test-chat", AiCapability.Chat);
    }

    [Fact]
    public async Task Chain_Chat_ExecutesThroughPipeline()
    {
        var fake = new FakePipeline("test response");

        using (Client.With(fake))
        {
            var result = await Chain.Create()
                .Chat("hello")
                .Run();

            result.Text.Should().Be("test response");
        }
    }

    [Fact]
    public async Task Chain_SystemAndChat_ExecutesThroughPipeline()
    {
        // ChainExecutor builds ChatOptions with the system message but currently
        // calls Client.ChatResult(message, ct) — so the system prompt is stored
        // on the chain definition but not forwarded to the pipeline request.
        // This test verifies the chain executes without error and produces output.
        var fake = new FakePipeline("system prompt response");

        using (Client.With(fake))
        {
            var result = await Chain.Create()
                .System("You are a helpful assistant")
                .Chat("What is Koan?")
                .Run();

            result.Text.Should().Be("system prompt response");
            result.Metrics.Steps.Should().Be(1);
        }
    }

    [Fact]
    public async Task Chain_WithVariables_ResolvesTemplates()
    {
        AiChatRequest? captured = null;
        var fake = new FakePipeline("Greetings", onPrompt: r => captured = r);

        using (Client.With(fake))
        {
            await Chain.Create()
                .Chat("Hello {name}")
                .Run(new { name = "World" });

            captured.Should().NotBeNull();
            var userMsg = captured!.Messages.Last(m => m.Role == "user");
            userMsg.Content.Should().Be("Hello World");
        }
    }

    [Fact]
    public async Task Chain_MultipleSteps_ExecutesAll()
    {
        var callCount = 0;
        var fake = new FakePipeline("step result", onPrompt: _ => callCount++);

        using (Client.With(fake))
        {
            var result = await Chain.Create()
                .Chat("step one")
                .Chat("step two: {output}")
                .Run();

            result.Text.Should().Be("step result");
            callCount.Should().Be(2, "both chat steps should have executed");
            result.Metrics.Steps.Should().Be(2);
        }
    }

    [Fact]
    public async Task Chain_Metrics_ReturnsStepCountAndDuration()
    {
        var fake = new FakePipeline("result", tokensIn: 5, tokensOut: 3);

        using (Client.With(fake))
        {
            var result = await Chain.Create()
                .Chat("hello")
                .Run();

            result.Metrics.Steps.Should().Be(1);
            result.Metrics.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
    }

    public void Dispose() => _fixture.Dispose();
}
