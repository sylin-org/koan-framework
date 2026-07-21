using System.Text.RegularExpressions;
using Koan.AI.Connector.HuggingFace.Api;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Shared;
using Koan.AI.Models;
using Koan.AI.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.AI.Connector.HuggingFace;

/// <summary>
/// HuggingFace Hub adapter. Declares Pull and ModelList capabilities for
/// model discovery and download. Follows the adapter capability pattern.
/// </summary>
internal sealed class HuggingFaceAdapter : IAiAdapter
{
    private readonly HuggingFaceClient _client;
    private readonly HuggingFaceOptions _options;
    private readonly ILogger<HuggingFaceAdapter> _logger;

    /// <summary>
    /// Pattern matching HuggingFace model IDs: <c>{org}/{name}</c> (e.g., "BAAI/bge-large-en-v1.5").
    /// </summary>
    private static readonly Regex HfIdPattern = new(@"^[a-zA-Z0-9\-_.]+/[a-zA-Z0-9\-_.]+$", RegexOptions.Compiled);

    /// <summary>
    /// File extensions ranked by preference for each <see cref="ModelFormat"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<ModelFormat, string[]> FormatExtensions =
        new Dictionary<ModelFormat, string[]>
        {
            [ModelFormat.SafeTensors] = [".safetensors"],
            [ModelFormat.GGUF] = [".gguf"],
            [ModelFormat.ONNX] = [".onnx"],
            [ModelFormat.PyTorch] = [".bin", ".pt", ".pth"],
            [ModelFormat.CoreML] = [".mlmodel", ".mlpackage"],
            [ModelFormat.OpenVINO] = [".xml"]
        };

    /// <summary>
    /// Default preference order when no format is specified.
    /// </summary>
    private static readonly string[] DefaultExtensionPriority =
        [".safetensors", ".gguf", ".onnx", ".bin", ".pt"];

    public HuggingFaceAdapter(
        HuggingFaceClient client,
        IOptions<HuggingFaceOptions> options,
        ILogger<HuggingFaceAdapter> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string Id => "huggingface";
    public string Name => "HuggingFace Hub";
    public string Type => "huggingface";

    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>
    {
        AiCapability.Pull,
        AiCapability.ModelList
    };

    public IAiModelManager? ModelManager => _modelManager ??= new HuggingFaceModelManager(this, _client, _options, _logger);
    private HuggingFaceModelManager? _modelManager;

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
    {
        // HuggingFace Hub doesn't list all models without a query;
        // return empty for enumeration and rely on search.
        return [];
    }

    // ── Internal helpers used by ModelManager ──

    internal bool CanHandle(string modelId) =>
        !string.IsNullOrWhiteSpace(modelId) && HfIdPattern.IsMatch(modelId);

    internal static ModelEntry MapToModelEntry(HfModelInfo info) => new()
    {
        HubId = info.Id,
        Origin = ModelOrigin.HuggingFace,
        Tags = info.Tags,
        License = info.CardData?.License,
        Capabilities = MapPipelineTagToCapabilities(info.PipelineTag),
        Format = InferFormatFromLibrary(info.LibraryName)
    };

    internal static HfFileInfo? SelectModelFile(IReadOnlyList<HfFileInfo> files, ModelFormat? preferredFormat)
    {
        if (files.Count == 0) return null;

        var extensions = preferredFormat.HasValue && FormatExtensions.TryGetValue(preferredFormat.Value, out var exts)
            ? exts
            : DefaultExtensionPriority;

        foreach (var ext in extensions)
        {
            var match = files
                .Where(f => f.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Lfs?.Size ?? f.Size)
                .FirstOrDefault();

            if (match is not null) return match;
        }

        return files.OrderByDescending(f => f.Lfs?.Size ?? f.Size).FirstOrDefault();
    }

    internal static ModelFormat DetectFormat(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".safetensors" => ModelFormat.SafeTensors,
            ".gguf" => ModelFormat.GGUF,
            ".onnx" => ModelFormat.ONNX,
            ".bin" or ".pt" or ".pth" => ModelFormat.PyTorch,
            ".mlmodel" or ".mlpackage" => ModelFormat.CoreML,
            ".xml" => ModelFormat.OpenVINO,
            _ => ModelFormat.SafeTensors
        };
    }

    private static List<ModelCapability> MapPipelineTagToCapabilities(string? pipelineTag)
    {
        if (string.IsNullOrWhiteSpace(pipelineTag)) return [];

        return pipelineTag.ToLowerInvariant() switch
        {
            "text-generation" or "text2text-generation" or "conversational" => [ModelCapability.Chat],
            "feature-extraction" or "sentence-similarity" => [ModelCapability.Embed],
            "image-text-to-text" or "visual-question-answering" => [ModelCapability.Vision, ModelCapability.Chat],
            "image-to-text" or "document-question-answering" => [ModelCapability.Ocr],
            "automatic-speech-recognition" => [ModelCapability.Transcription],
            "text-to-image" or "image-to-image" => [ModelCapability.ImageGeneration],
            "text-classification" or "token-classification" or "zero-shot-classification" => [ModelCapability.Chat],
            _ => []
        };
    }

    private static ModelFormat InferFormatFromLibrary(string? libraryName)
    {
        if (string.IsNullOrWhiteSpace(libraryName)) return ModelFormat.SafeTensors;

        return libraryName.ToLowerInvariant() switch
        {
            "onnx" => ModelFormat.ONNX,
            "gguf" or "llama.cpp" => ModelFormat.GGUF,
            "coreml" => ModelFormat.CoreML,
            "openvino" => ModelFormat.OpenVINO,
            _ => ModelFormat.SafeTensors
        };
    }
}

/// <summary>
/// Model manager for HuggingFace that handles pull/download operations.
/// </summary>
internal sealed class HuggingFaceModelManager : IAiModelManager
{
    private readonly HuggingFaceAdapter _adapter;
    private readonly HuggingFaceClient _client;
    private readonly HuggingFaceOptions _options;
    private readonly ILogger _logger;

    public HuggingFaceModelManager(
        HuggingFaceAdapter adapter,
        HuggingFaceClient client,
        HuggingFaceOptions options,
        ILogger logger)
    {
        _adapter = adapter;
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<AiModelOperationResult> EnsureInstalled(
        AiModelOperationRequest request, CancellationToken ct = default)
    {
        var modelId = request.Model;

        if (!_adapter.CanHandle(modelId))
        {
            return new AiModelOperationResult
            {
                Success = false,
                Message = $"'{modelId}' is not a valid HuggingFace model ID (expected org/name format)."
            };
        }

        var info = await _client.GetModelInfo(modelId, ct);
        if (info is null)
        {
            return new AiModelOperationResult
            {
                Success = false,
                Message = $"Model '{modelId}' not found on HuggingFace Hub."
            };
        }

        var files = await _client.ListFiles(modelId, ct);
        var modelFile = HuggingFaceAdapter.SelectModelFile(files, null);

        if (modelFile is null)
        {
            return new AiModelOperationResult
            {
                Success = false,
                Message = $"No suitable model file found for '{modelId}'."
            };
        }

        var modelDir = Path.Combine(_options.CacheDirectory, modelId.Replace('/', Path.DirectorySeparatorChar));
        var outputPath = Path.Combine(modelDir, modelFile.FileName);

        if (!File.Exists(outputPath))
        {
            await _client.DownloadFile(modelId, modelFile.FileName, outputPath, progress: null, ct);
        }
        else
        {
            _logger.LogInformation("Model file already cached: {Path}", outputPath);
        }

        // Download config.json if present
        var configFile = files.FirstOrDefault(f =>
            string.Equals(f.FileName, "config.json", StringComparison.OrdinalIgnoreCase));

        if (configFile is not null)
        {
            var configPath = Path.Combine(modelDir, "config.json");
            if (!File.Exists(configPath))
            {
                await _client.DownloadFile(modelId, "config.json", configPath, progress: null, ct);
            }
        }

        // Download tokenizer files
        var tokenizerFiles = files
            .Where(f => f.FileName.StartsWith("tokenizer", StringComparison.OrdinalIgnoreCase)
                        && f.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var tokFile in tokenizerFiles)
        {
            var tokPath = Path.Combine(modelDir, tokFile.FileName);
            if (!File.Exists(tokPath))
            {
                await _client.DownloadFile(modelId, tokFile.FileName, tokPath, progress: null, ct);
            }
        }

        var entry = HuggingFaceAdapter.MapToModelEntry(info);
        entry.LocalPath = outputPath;
        entry.DiskSizeBytes = modelFile.Lfs?.Size ?? modelFile.Size;
        entry.Format = HuggingFaceAdapter.DetectFormat(modelFile.FileName);

        return new AiModelOperationResult
        {
            Success = true,
            Message = $"Model '{modelId}' downloaded to {outputPath}.",
            Model = new AiModelDescriptor
            {
                Name = info.Id,
                AdapterType = "huggingface"
            }
        };
    }

    public Task<AiModelOperationResult> Refresh(
        AiModelOperationRequest request, CancellationToken ct = default)
    {
        // Re-download by delegating to EnsureInstalledAsync (could delete cache first)
        return EnsureInstalled(request, ct);
    }

    public Task<AiModelOperationResult> Flush(
        AiModelOperationRequest request, CancellationToken ct = default)
    {
        var modelId = request.Model;
        var modelDir = Path.Combine(_options.CacheDirectory, modelId.Replace('/', Path.DirectorySeparatorChar));

        if (Directory.Exists(modelDir))
        {
            Directory.Delete(modelDir, recursive: true);
            _logger.LogInformation("Flushed cached model: {ModelId}", modelId);
        }

        return Task.FromResult(new AiModelOperationResult
        {
            Success = true,
            Message = $"Model '{modelId}' flushed from local cache."
        });
    }

    public Task<IReadOnlyList<AiModelDescriptor>> ListManagedModels(CancellationToken ct = default)
    {
        // List models in the cache directory
        IReadOnlyList<AiModelDescriptor> empty = [];
        return Task.FromResult(empty);
    }
}
