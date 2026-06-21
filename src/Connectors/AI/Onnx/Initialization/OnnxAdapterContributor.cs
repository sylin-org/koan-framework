using System;
using System.Collections.Generic;
using FastBertTokenizer;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace Koan.AI.Connector.Onnx.Initialization;

/// <summary>
/// Builds and registers the in-process ONNX embedding adapter at startup. Reference = Intent with an opt-in:
/// the connector activates only when a model is configured (<c>Koan:Ai:Onnx:ModelPath</c>), because an
/// embedder with no model is meaningless. A configured-but-missing model/vocab fails loud — silent
/// no-embedding would be worse than a clear boot error.
/// </summary>
internal sealed class OnnxAdapterContributor : IAiAdapterContributor
{
    public async ValueTask Contribute(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = services.GetService<IOptions<OnnxOptions>>()?.Value ?? new OnnxOptions();
        var registry = services.GetRequiredService<IAiAdapterRegistry>();
        var sourceRegistry = services.GetRequiredService<IAiSourceRegistry>();
        var logger = services.GetService<ILogger<OnnxAdapterContributor>>() ?? NullLogger<OnnxAdapterContributor>.Instance;

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            logger.LogInformation("[ONNX] No model configured (Koan:Ai:Onnx:ModelPath); in-process embeddings inactive.");
            return;
        }

        // Relative paths resolve against the app base dir, so a bundled model (copied next to the exe)
        // works the same whether run from the project, published, or single-file.
        var modelPath = Resolve(options.ModelPath);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ONNX embedding model not found at '{modelPath}'. Set Koan:Ai:Onnx:ModelPath to a valid ONNX sentence-embedding model.",
                modelPath);

        var vocabPath = options.VocabPath is { Length: > 0 } v
            ? Resolve(v)
            : Path.Combine(Path.GetDirectoryName(modelPath) ?? ".", "vocab.txt");
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException(
                $"ONNX WordPiece vocabulary not found at '{vocabPath}'. Set Koan:Ai:Onnx:VocabPath, or place vocab.txt beside the model.",
                vocabPath);

        var tokenizer = new BertTokenizer();
        using (var reader = File.OpenText(vocabPath))
            await tokenizer.LoadVocabularyAsync(reader, convertInputToLowercase: options.LowercaseInput).ConfigureAwait(false);

        var session = new InferenceSession(modelPath);
        var adapter = new OnnxEmbeddingAdapter(options, session, tokenizer);
        registry.Add(adapter);

        RegisterSource(sourceRegistry, adapter);

        logger.LogInformation("[ONNX] In-process embedding adapter registered: {Model} (dim {Dimension}) from {Path}.",
            options.ModelName, adapter.Dimension, modelPath);
    }

    /// <summary>
    /// Publishes the in-process adapter as an AI <i>source</i> so the high-level facade
    /// (<c>Koan.AI.Client.Embed</c>) and the <c>[Embedding]</c> worker route to it — not just the low-level
    /// adapter registry. Mirrors the Ollama source: the router elects a source by the <c>Embedding</c>
    /// capability and resolves its adapter via <c>IAiAdapterRegistry.Get(source.Provider)</c> (which matches
    /// the adapter's provider-level <see cref="OnnxEmbeddingAdapter.Id"/> = <c>onnx</c>), and the embedded
    /// model is the source's default model — a usage-time concern carried in the <c>Embedding</c> capability,
    /// exactly as Ollama carries its default model. The single member's connection string is a placeholder:
    /// an in-process adapter needs no endpoint.
    /// </summary>
    private static void RegisterSource(IAiSourceRegistry sourceRegistry, OnnxEmbeddingAdapter adapter)
    {
        var capabilities = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase)
        {
            // Embedding-only with the embedded model as the default. AutoDownload is an Ollama-ism; the model
            // travels with the app here, so it must be off.
            ["Embedding"] = new AiCapabilityConfig { Model = adapter.DefaultModel, AutoDownload = false },
        };

        var source = new AiSourceDefinition
        {
            Name = "onnx",
            Provider = adapter.Id, // "onnx" — provider-level, so the router resolves the adapter by Get(Provider)
            Priority = 50,
            Policy = "Fallback",
            Members = new List<AiMemberDefinition>
            {
                new()
                {
                    Name = "onnx::inproc",
                    ConnectionString = "inproc://onnx",
                    Order = 0,
                    Capabilities = capabilities,
                    Origin = "in-process",
                    IsAutoDiscovered = false,
                    HealthState = MemberHealthState.Healthy,
                },
            },
            Capabilities = capabilities,
            Origin = "in-process",
            IsAutoDiscovered = false,
        };

        sourceRegistry.RegisterSource(source);
    }

    private static string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
