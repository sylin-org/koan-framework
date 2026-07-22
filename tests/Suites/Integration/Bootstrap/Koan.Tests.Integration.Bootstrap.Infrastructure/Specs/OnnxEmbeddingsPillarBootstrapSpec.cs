using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.AI;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.Core;
using Koan.AI.Contracts;
using Koan.Core.Hosting.App;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Infrastructure.Specs;

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

    [Fact(Explicit = true)]
    public async Task AddKoan_registers_onnx_embedder_and_embeds_semantically()
    {
        var modelPath = FindModelPath();
        Assert.SkipWhen(modelPath is null, "ONNX model asset not present (assets/models/all-MiniLM-L6-v2).");

        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Communication:TransportProvider", "in-process")
            .WithSetting("Koan:Communication:EventsProvider", "in-process")
            .WithSetting("Koan:Communication:FrameworkSignalsProvider", "in-process")
            .WithSetting("Koan:Communication:FrameworkBroadcastsProvider", "in-process")
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

    [Fact(Explicit = true)]
    public async Task Client_Embed_routes_to_the_in_process_onnx_source()
    {
        var modelPath = FindModelPath();
        Assert.SkipWhen(modelPath is null, "ONNX model asset not present (assets/models/all-MiniLM-L6-v2).");

        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Communication:TransportProvider", "in-process")
            .WithSetting("Koan:Communication:EventsProvider", "in-process")
            .WithSetting("Koan:Communication:FrameworkSignalsProvider", "in-process")
            .WithSetting("Koan:Communication:FrameworkBroadcastsProvider", "in-process")
            .WithSetting("Koan:Ai:Onnx:ModelPath", modelPath!)
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // The contributor must publish the adapter as an AI *source* — not just an adapter — so the router
        // can elect it for the Embedding capability and resolve its adapter via Get(source.Provider). The
        // Provider therefore has to equal the adapter's Id (which carries the model name).
        var adapter = host.Services.GetRequiredService<IAiAdapterRegistry>()
            .All.OfType<IEmbedAdapter>().Single(a => a.Type == "onnx");
        var embedSources = host.Services.GetRequiredService<IAiSourceRegistry>()
            .GetSourcesWithCapability("Embedding");
        embedSources.Should().ContainSingle(s => s.Provider == adapter.Id,
            "the in-process ONNX source must advertise Embedding and point Provider at the adapter Id");

        // End-to-end through the user-facing facade: Client.Embed must route through the source registry to
        // the in-process ONNX adapter — the contract the sample's seed/search path relies on. AppHost.Current
        // is the static facade's resolution root; set it for this host (the suite runs serially) and restore.
        var previous = AppHost.Current;
        AppHost.Current = host.Services;
        try
        {
            var vector = await Client.Embed("a basket of ripe red tomatoes");
            vector.Length.Should().Be(384, "Client.Embed must reach the ONNX adapter and return its 384-dim vector");
            vector.Any(v => v != 0f).Should().BeTrue("a real embedding is not all-zero");
        }
        finally
        {
            AppHost.Current = previous;
        }
    }

    [Fact(Explicit = true)]
    public async Task Misconfigured_model_path_fails_boot_with_corrective_error()
    {
        // Explicit configuration is intent. A missing model must fail boot rather than silently removing
        // the provider the application asked Koan to activate.
        var builder = KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Communication:TransportProvider", "in-process")
            .WithSetting("Koan:Communication:EventsProvider", "in-process")
            .WithSetting("Koan:Communication:FrameworkSignalsProvider", "in-process")
            .WithSetting("Koan:Communication:FrameworkBroadcastsProvider", "in-process")
            .WithSetting("Koan:Ai:Onnx:ModelPath", "does-not-exist.onnx")
            .ConfigureServices(services => services.AddKoan());

        var start = async () => await builder.StartAsync();

        await start.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*ONNX embedding model not found*Koan:Ai:Onnx:ModelPath*");
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9);
    }
}
