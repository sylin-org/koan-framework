using FluentAssertions;
using Koan.AI.Integration.Tests.Fixtures;
using Koan.Core.AI;
using Xunit;

namespace Koan.AI.Integration.Tests;

public sealed class CapabilityMatrixTests
{
    [Fact]
    public void OllamaLikeAdapter_HasExpectedCapabilities()
    {
        var adapter = new TestCapableAdapter("ollama",
            AiCapability.Chat, AiCapability.Embed, AiCapability.Vision,
            AiCapability.Pull, AiCapability.ModelRemove, AiCapability.ModelList,
            AiCapability.ServeGGUF, AiCapability.Streaming, AiCapability.Tools);

        adapter.HasCapability(AiCapability.Chat).Should().BeTrue();
        adapter.HasCapability(AiCapability.Embed).Should().BeTrue();
        adapter.HasCapability(AiCapability.Vision).Should().BeTrue();
        adapter.HasCapability(AiCapability.Pull).Should().BeTrue();
        adapter.HasCapability(AiCapability.ModelRemove).Should().BeTrue();
        adapter.HasCapability(AiCapability.ModelList).Should().BeTrue();
        adapter.HasCapability(AiCapability.ServeGGUF).Should().BeTrue();
        adapter.HasCapability(AiCapability.Streaming).Should().BeTrue();
        adapter.HasCapability(AiCapability.Tools).Should().BeTrue();
        adapter.ModelManager.Should().NotBeNull("Pull capability implies a model manager");
    }

    [Fact]
    public void HuggingFaceLikeAdapter_HasExpectedCapabilities()
    {
        var adapter = new TestCapableAdapter("huggingface",
            AiCapability.Chat, AiCapability.Embed,
            AiCapability.Pull, AiCapability.Push, AiCapability.ModelList,
            AiCapability.ServeSafeTensors, AiCapability.Streaming);

        adapter.HasCapability(AiCapability.Chat).Should().BeTrue();
        adapter.HasCapability(AiCapability.Embed).Should().BeTrue();
        adapter.HasCapability(AiCapability.Pull).Should().BeTrue();
        adapter.HasCapability(AiCapability.Push).Should().BeTrue();
        adapter.HasCapability(AiCapability.ModelList).Should().BeTrue();
        adapter.HasCapability(AiCapability.ServeSafeTensors).Should().BeTrue();
        adapter.HasCapability(AiCapability.Streaming).Should().BeTrue();
    }

    [Fact]
    public void OnnxLikeAdapter_HasExpectedCapabilities()
    {
        var adapter = new TestCapableAdapter("onnx",
            AiCapability.Embed, AiCapability.ServeONNX, AiCapability.BatchEmbed);

        adapter.HasCapability(AiCapability.Embed).Should().BeTrue();
        adapter.HasCapability(AiCapability.ServeONNX).Should().BeTrue();
        adapter.HasCapability(AiCapability.BatchEmbed).Should().BeTrue();

        adapter.HasCapability(AiCapability.Chat).Should().BeFalse();
        adapter.HasCapability(AiCapability.Pull).Should().BeFalse();
        adapter.HasCapability(AiCapability.Train).Should().BeFalse();
    }

    [Fact]
    public void PythonSidecarLikeAdapter_HasExpectedCapabilities()
    {
        var adapter = new TestCapableAdapter("python-sidecar",
            AiCapability.Train, AiCapability.Align,
            AiCapability.Convert, AiCapability.Quantize);

        adapter.HasCapability(AiCapability.Train).Should().BeTrue();
        adapter.HasCapability(AiCapability.Align).Should().BeTrue();
        adapter.HasCapability(AiCapability.Convert).Should().BeTrue();
        adapter.HasCapability(AiCapability.Quantize).Should().BeTrue();

        adapter.HasCapability(AiCapability.Chat).Should().BeFalse();
        adapter.HasCapability(AiCapability.Pull).Should().BeFalse();
    }

    [Fact]
    public void ServeCapability_MatchesModelFormat()
    {
        AiCapability.ServeGGUF.Should().Be("Serve.GGUF");
        AiCapability.ServeSafeTensors.Should().Be("Serve.SafeTensors");
        AiCapability.ServeONNX.Should().Be("Serve.ONNX");
    }
}
