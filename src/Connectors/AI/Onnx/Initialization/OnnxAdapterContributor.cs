using FastBertTokenizer;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
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

        logger.LogInformation("[ONNX] In-process embedding adapter registered: {Model} (dim {Dimension}) from {Path}.",
            options.ModelName, adapter.Dimension, modelPath);
    }

    private static string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
