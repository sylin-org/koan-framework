using System;
using System.Collections.Generic;
using FastBertTokenizer;
using Koan.AI.Contracts.Sources;
using Koan.AI.Providers;
using Koan.Core.Logging;
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
internal sealed class OnnxAdapterContributor : IAiProviderActivator
{
    public ValueTask<AiProviderActivation?> Activate(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = services.GetService<IOptions<OnnxOptions>>()?.Value ?? new OnnxOptions();
        var logger = services.GetService<ILogger<OnnxAdapterContributor>>() ?? NullLogger<OnnxAdapterContributor>.Instance;

        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            KoanLog.BootInfo(logger, LogAction, "inactive",
                ("reason", "model-not-configured"));
            return ValueTask.FromResult<AiProviderActivation?>(null);
        }

        var adapter = services.GetRequiredService<OnnxEmbeddingAdapter>();
        var source = BuildSource(adapter);

        KoanLog.BootInfo(logger, LogAction, "ready",
            ("model", options.ModelName),
            ("dimension", adapter.Dimension),
            ("path", Resolve(options.ModelPath)));

        return ValueTask.FromResult<AiProviderActivation?>(new AiProviderActivation
        {
            Adapter = adapter,
            Sources = [source]
        });
    }

    internal static OnnxEmbeddingAdapter CreateAdapter(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<OnnxOptions>>().Value;
        var configuredModelPath = options.ModelPath
            ?? throw new InvalidOperationException("Koan:Ai:Onnx:ModelPath is required when the ONNX provider activates.");
        var modelPath = Resolve(configuredModelPath);
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
            tokenizer.LoadVocabularyAsync(reader, convertInputToLowercase: options.LowercaseInput)
                .GetAwaiter().GetResult();

        var session = new InferenceSession(modelPath);
        return new OnnxEmbeddingAdapter(options, session, tokenizer);
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
    private static AiSourceDefinition BuildSource(OnnxEmbeddingAdapter adapter)
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

        return source;
    }

    private static string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    private const string LogAction = "onnx.activation";
}
