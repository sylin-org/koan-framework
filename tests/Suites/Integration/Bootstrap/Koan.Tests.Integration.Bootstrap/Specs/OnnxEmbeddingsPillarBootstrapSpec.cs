using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.Core;
using Koan.Core.AI;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the in-process embeddings floor (per ARCH-0079). Proves the ONNX connector, discovered
/// through real <c>AddKoan()</c>, builds an in-process <see cref="IEmbedAdapter"/> from a local model (no
/// model server) and produces semantically meaningful embeddings: related sentences rank closer than
/// unrelated ones, dimension is the model's 384. The model is a committed asset
/// (assets/models/all-MiniLM-L6-v2); the spec self-skips if it is absent.
/// </summary>
public sealed class OnnxEmbeddingsPillarBootstrapSpec
{
    private static string? FindModelPath([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile) ?? AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Koan.sln"))) dir = dir.Parent;
        if (dir is null) return null;
        var model = Path.Combine(dir.FullName, "assets", "models", "all-MiniLM-L6-v2", "model_quantized.onnx");
        return File.Exists(model) ? model : null;
    }

    [Fact]
    public async Task AddKoan_registers_onnx_embedder_and_embeds_semantically()
    {
        var modelPath = FindModelPath();
        Assert.SkipWhen(modelPath is null, "ONNX model asset not present (assets/models/all-MiniLM-L6-v2).");

        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Ai:Onnx:ModelPath", modelPath!)
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var registry = host.Services.GetRequiredService<IAiAdapterRegistry>();
        var onnx = registry.All.OfType<IEmbedAdapter>().FirstOrDefault(a => a.Type == "onnx");
        onnx.Should().NotBeNull("the ONNX connector must register an embed adapter when a model is configured");
        onnx!.Capabilities.Should().Contain(AiCapability.Embed);

        var resp = await onnx.Embed(new AiEmbeddingsRequest
        {
            Input = { "I love cats", "I adore felines", "the quarterly report is late" }
        });

        resp.Vectors.Should().HaveCount(3);
        resp.Dimension.Should().Be(384);
        resp.Vectors[0].Length.Should().Be(384);

        var related = Cosine(resp.Vectors[0], resp.Vectors[1]);   // cats ~ felines
        var unrelated = Cosine(resp.Vectors[0], resp.Vectors[2]); // cats ~ quarterly report
        related.Should().BeGreaterThan(unrelated,
            "a real embedding model must place 'cats'~'felines' closer than 'cats'~'quarterly report'");
    }

    [Fact]
    public async Task Misconfigured_model_path_degrades_cleanly_without_registering()
    {
        // A configured-but-missing model must not silently register a broken embedder. The contributor
        // fails loud; the AI initializer's swallow-and-warn policy turns that into "no adapter", never a
        // wrong one.
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Ai:Onnx:ModelPath", "does-not-exist.onnx")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var registry = host.Services.GetRequiredService<IAiAdapterRegistry>();
        registry.All.OfType<IEmbedAdapter>().Any(a => a.Type == "onnx")
            .Should().BeFalse("a missing model must yield no ONNX adapter, not a broken one");
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9);
    }
}
