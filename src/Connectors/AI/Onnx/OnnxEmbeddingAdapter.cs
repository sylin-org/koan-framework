using FastBertTokenizer;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Koan.AI.Connector.Onnx;

/// <summary>
/// In-process embedding adapter over ONNX Runtime. Tokenizes with a WordPiece <see cref="BertTokenizer"/>,
/// runs a local sentence-embedding model, mean-pools the token embeddings under the attention mask, and
/// (by default) L2-normalizes — the sentence-transformers recipe, entirely in one process. Structurally
/// embed-only: it implements <see cref="IEmbedAdapter"/> and declares <see cref="AiCapability.Embed"/> and
/// nothing else, so chat/vision/etc. fail loud rather than silently mis-serving.
/// </summary>
internal sealed class OnnxEmbeddingAdapter : IEmbedAdapter, IDisposable
{
    private readonly OnnxOptions _options;
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly string[] _inputNames;

    public OnnxEmbeddingAdapter(OnnxOptions options, InferenceSession session, BertTokenizer tokenizer)
    {
        _options = options;
        _session = session;
        _tokenizer = tokenizer;
        _inputNames = session.InputMetadata.Keys.ToArray();

        // Hidden size from the model's output metadata when static; otherwise the configured fallback.
        var outDims = session.OutputMetadata.Values.First().Dimensions;
        var hidden = outDims.Length > 0 ? outDims[^1] : -1;
        Dimension = hidden > 0 ? hidden : options.Dimension;
    }

    public int Dimension { get; }

    // Provider-level identity, mirroring the Ollama adapter (Id == Type == "ollama"): the adapter is the
    // in-process ONNX *provider*, and the embedded model is its default — a usage-time concern surfaced via
    // the source's Embedding capability, not baked into the adapter's identity. The registry dedupes by Id,
    // so one ONNX provider serves the one model the app embeds (the in-process single-model tier).
    public string Id => "onnx";
    public string Name => $"ONNX ({_options.ModelName})";
    public string Type => "onnx";

    /// <summary>The default (and, for the in-process tier, only) model this provider serves.</summary>
    public string DefaultModel => _options.ModelName;

    public IReadOnlySet<string> Capabilities { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AiCapability.Embed };

    public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModelDescriptor>>(new[]
        {
            new AiModelDescriptor
            {
                Name = _options.ModelName,
                Family = "sentence-transformers",
                EmbeddingDim = Dimension,
                AdapterId = Id,
                AdapterType = Type,
            }
        });

    public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var vectors = new List<float[]>(request.Input.Count);
        foreach (var text in request.Input)
        {
            ct.ThrowIfCancellationRequested();
            vectors.Add(EmbedOne(text ?? string.Empty));
        }
        return Task.FromResult(new AiEmbeddingsResponse { Vectors = vectors, Dimension = Dimension });
    }

    private float[] EmbedOne(string text)
    {
        var (inputIds, attentionMask, tokenTypeIds) = _tokenizer.Encode(text, _options.MaxTokens);
        var seqLen = inputIds.Length;

        var idsTensor = new DenseTensor<long>(inputIds.ToArray(), new[] { 1, seqLen });
        var maskTensor = new DenseTensor<long>(attentionMask.ToArray(), new[] { 1, seqLen });
        var typeTensor = new DenseTensor<long>(tokenTypeIds.ToArray(), new[] { 1, seqLen });

        var inputs = new List<NamedOnnxValue>(_inputNames.Length);
        foreach (var name in _inputNames)
        {
            var tensor = name.ToLowerInvariant() switch
            {
                "attention_mask" => maskTensor,
                "token_type_ids" => typeTensor,
                _ => idsTensor, // input_ids (and any unexpected name defaults to ids)
            };
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, tensor));
        }

        using var results = _session.Run(inputs);
        var output = results[0].AsTensor<float>();
        var pooled = MeanPool(output, attentionMask.Span, seqLen);
        if (_options.NormalizeEmbeddings) L2Normalize(pooled);
        return pooled;
    }

    /// <summary>Mean-pool token embeddings under the attention mask, or return a pre-pooled 2-D output as-is.</summary>
    private static float[] MeanPool(Tensor<float> output, ReadOnlySpan<long> mask, int seqLen)
    {
        // Pre-pooled output: [batch, hidden].
        if (output.Dimensions.Length == 2)
        {
            var hidden2 = output.Dimensions[1];
            var vec = new float[hidden2];
            for (var h = 0; h < hidden2; h++) vec[h] = output[0, h];
            return vec;
        }

        // Token embeddings: [batch, seq, hidden] — average the non-masked positions.
        var hidden = output.Dimensions[^1];
        var sum = new float[hidden];
        long count = 0;
        for (var t = 0; t < seqLen; t++)
        {
            if (t < mask.Length && mask[t] == 0) continue;
            count++;
            for (var h = 0; h < hidden; h++) sum[h] += output[0, t, h];
        }
        if (count == 0) count = 1;
        for (var h = 0; h < hidden; h++) sum[h] /= count;
        return sum;
    }

    private static void L2Normalize(float[] vector)
    {
        double norm = 0;
        foreach (var v in vector) norm += (double)v * v;
        norm = Math.Sqrt(norm);
        if (norm <= 0) return;
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
    }

    public void Dispose() => _session.Dispose();
}
