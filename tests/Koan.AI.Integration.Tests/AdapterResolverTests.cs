using AwesomeAssertions;
using Koan.AI.Integration.Tests.Fixtures;
using Koan.AI.Resolution;
using Koan.Core.AI;
using Xunit;

namespace Koan.AI.Integration.Tests;

public sealed class AdapterResolverTests
{
    [Fact]
    public void SingleAdapter_WithCapability_ResolvesUnambiguously()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Pull));

        var result = AdapterResolver.Resolve(registry, AiCapability.Pull);

        result.Id.Should().Be("ollama");
    }

    [Fact]
    public void SingleAdapter_WithoutCapability_Throws()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Chat));

        var act = () => AdapterResolver.Resolve(registry, AiCapability.Pull);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No adapter*Pull*");
    }

    [Fact]
    public void MultipleAdapters_OneWithCapability_ResolvesCorrectOne()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Chat, AiCapability.Pull, AiCapability.ServeGGUF));
        registry.Add(new TestCapableAdapter("onnx", AiCapability.Embed, AiCapability.ServeONNX));

        var result = AdapterResolver.Resolve(registry, AiCapability.Pull);

        result.Id.Should().Be("ollama");
    }

    [Fact]
    public void MultipleAdapters_BothWithCapability_ThrowsAmbiguous()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Pull));
        registry.Add(new TestCapableAdapter("huggingface", AiCapability.Pull));

        var act = () => AdapterResolver.Resolve(registry, AiCapability.Pull);

        act.Should().Throw<AmbiguousAdapterException>()
            .Which.AdapterIds.Should().Contain("ollama").And.Contain("huggingface");
    }

    [Fact]
    public void MultipleAdapters_BothWithCapability_ExplicitTarget_ResolvesCorrectOne()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Pull));
        registry.Add(new TestCapableAdapter("huggingface", AiCapability.Pull));

        var result = AdapterResolver.Resolve(registry, AiCapability.Pull, target: "ollama");

        result.Id.Should().Be("ollama");
    }

    [Fact]
    public void ExplicitTarget_NotRegistered_Throws()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Pull));

        var act = () => AdapterResolver.Resolve(registry, AiCapability.Pull, target: "nonexistent");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'nonexistent' not registered*");
    }

    [Fact]
    public void ExplicitTarget_WrongCapability_Throws()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Chat));

        var act = () => AdapterResolver.Resolve(registry, AiCapability.Pull, target: "ollama");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'ollama'*does not have*Pull*capability*");
    }

    [Fact]
    public void ResolveAll_ReturnsAllMatching()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Embed, AiCapability.Chat));
        registry.Add(new TestCapableAdapter("onnx", AiCapability.Embed, AiCapability.ServeONNX));
        registry.Add(new TestCapableAdapter("openai", AiCapability.Chat));

        var result = AdapterResolver.ResolveAll(registry, AiCapability.Embed);

        result.Should().HaveCount(2);
        result.Select(a => a.Id).Should().Contain("ollama").And.Contain("onnx");
    }

    [Fact]
    public void ResolveAll_NoneMatching_ReturnsEmpty()
    {
        var registry = new TestAdapterRegistry();
        registry.Add(new TestCapableAdapter("ollama", AiCapability.Chat));

        var result = AdapterResolver.ResolveAll(registry, AiCapability.Train);

        result.Should().BeEmpty();
    }

    [Fact]
    public void NoAdapters_Throws_WithClearMessage()
    {
        var registry = new TestAdapterRegistry();

        var act = () => AdapterResolver.Resolve(registry, AiCapability.Chat);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No adapter*Chat*");
    }
}
