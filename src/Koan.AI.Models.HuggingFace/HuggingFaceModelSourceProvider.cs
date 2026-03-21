using System.Text.RegularExpressions;
using Koan.AI.Contracts.Shared;
using Koan.AI.Models.HuggingFace.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.AI.Models.HuggingFace;

/// <summary>
/// <see cref="IModelSourceProvider"/> implementation for HuggingFace Hub.
/// Supports search, metadata retrieval, and model file download with progress.
/// </summary>
internal sealed class HuggingFaceModelSourceProvider : IModelSourceProvider
{
    private readonly HuggingFaceClient _client;
    private readonly HuggingFaceOptions _options;
    private readonly ILogger<HuggingFaceModelSourceProvider> _logger;

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

    public HuggingFaceModelSourceProvider(
        HuggingFaceClient client,
        IOptions<HuggingFaceOptions> options,
        ILogger<HuggingFaceModelSourceProvider> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "huggingface";

    public bool CanHandle(string modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) && HfIdPattern.IsMatch(modelId);
    }

    public async Task<IReadOnlyList<ModelEntry>> SearchAsync(
        string query, int maxResults = 20, CancellationToken ct = default)
    {
        var hfModels = await _client.SearchAsync(query, maxResults, ct);
        return hfModels.Select(MapToModelEntry).ToList().AsReadOnly();
    }

    public async Task<ModelEntry?> GetMetadataAsync(string modelId, CancellationToken ct = default)
    {
        var info = await _client.GetModelInfoAsync(modelId, ct);
        return info is null ? null : MapToModelEntry(info);
    }

    public async Task<ModelEntry> PullAsync(
        string modelId, string targetDirectory,
        ModelFormat? preferredFormat = null,
        IProgress<ModelPullProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new ModelPullProgress { Phase = "Fetching metadata", Percent = 0 });

        var info = await _client.GetModelInfoAsync(modelId, ct)
            ?? throw new InvalidOperationException($"Model '{modelId}' not found on HuggingFace Hub.");

        progress?.Report(new ModelPullProgress { Phase = "Listing files", Percent = 5 });

        var files = await _client.ListFilesAsync(modelId, ct);
        var modelFile = SelectModelFile(files, preferredFormat);

        if (modelFile is null)
        {
            throw new InvalidOperationException(
                $"No suitable model file found for '{modelId}'. " +
                $"Available files: {string.Join(", ", files.Select(f => f.FileName))}");
        }

        var modelDir = Path.Combine(targetDirectory, modelId.Replace('/', Path.DirectorySeparatorChar));
        var outputPath = Path.Combine(modelDir, modelFile.FileName);

        if (!File.Exists(outputPath))
        {
            progress?.Report(new ModelPullProgress
            {
                Phase = "Downloading",
                Percent = 10,
                TotalBytes = modelFile.Lfs?.Size ?? modelFile.Size
            });

            await _client.DownloadFileAsync(modelId, modelFile.FileName, outputPath, progress, ct);
        }
        else
        {
            _logger.LogInformation("Model file already cached: {Path}", outputPath);
        }

        // Download config.json if present (needed for model loading)
        var configFile = files.FirstOrDefault(f =>
            string.Equals(f.FileName, "config.json", StringComparison.OrdinalIgnoreCase));

        if (configFile is not null)
        {
            var configPath = Path.Combine(modelDir, "config.json");

            if (!File.Exists(configPath))
            {
                await _client.DownloadFileAsync(modelId, "config.json", configPath, progress: null, ct);
            }
        }

        // Download tokenizer files if present
        var tokenizerFiles = files
            .Where(f => f.FileName.StartsWith("tokenizer", StringComparison.OrdinalIgnoreCase)
                        && f.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var tokFile in tokenizerFiles)
        {
            var tokPath = Path.Combine(modelDir, tokFile.FileName);

            if (!File.Exists(tokPath))
            {
                await _client.DownloadFileAsync(modelId, tokFile.FileName, tokPath, progress: null, ct);
            }
        }

        progress?.Report(new ModelPullProgress { Phase = "Complete", Percent = 100 });

        var fileSize = modelFile.Lfs?.Size ?? modelFile.Size;

        var entry = MapToModelEntry(info);
        entry.LocalPath = outputPath;
        entry.DiskSizeBytes = fileSize > 0 ? fileSize : new FileInfo(outputPath).Length;
        entry.Format = DetectFormat(modelFile.FileName);

        return entry;
    }

    // ── Private helpers ──

    private static HfFileInfo? SelectModelFile(IReadOnlyList<HfFileInfo> files, ModelFormat? preferredFormat)
    {
        if (files.Count == 0)
        {
            return null;
        }

        var extensions = preferredFormat.HasValue && FormatExtensions.TryGetValue(preferredFormat.Value, out var exts)
            ? exts
            : DefaultExtensionPriority;

        foreach (var ext in extensions)
        {
            var match = files
                .Where(f => f.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Lfs?.Size ?? f.Size)
                .FirstOrDefault();

            if (match is not null)
            {
                return match;
            }
        }

        // Fallback: largest file (likely the model weights)
        return files
            .OrderByDescending(f => f.Lfs?.Size ?? f.Size)
            .FirstOrDefault();
    }

    private static ModelFormat DetectFormat(string fileName)
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

    private static ModelEntry MapToModelEntry(HfModelInfo info)
    {
        return new ModelEntry
        {
            HubId = info.Id,
            Origin = ModelOrigin.HuggingFace,
            Tags = info.Tags,
            License = info.CardData?.License,
            Capabilities = MapPipelineTagToCapabilities(info.PipelineTag),
            Format = InferFormatFromLibrary(info.LibraryName)
        };
    }

    private static List<ModelCapability> MapPipelineTagToCapabilities(string? pipelineTag)
    {
        if (string.IsNullOrWhiteSpace(pipelineTag))
        {
            return [];
        }

        return pipelineTag.ToLowerInvariant() switch
        {
            "text-generation" or "text2text-generation" or "conversational" =>
                [ModelCapability.Chat],
            "feature-extraction" or "sentence-similarity" =>
                [ModelCapability.Embed],
            "image-text-to-text" or "visual-question-answering" =>
                [ModelCapability.Vision, ModelCapability.Chat],
            "image-to-text" or "document-question-answering" =>
                [ModelCapability.Ocr],
            "automatic-speech-recognition" =>
                [ModelCapability.Transcription],
            "text-to-image" or "image-to-image" =>
                [ModelCapability.ImageGeneration],
            "text-classification" or "token-classification" or "zero-shot-classification" =>
                [ModelCapability.Chat],
            _ => []
        };
    }

    private static ModelFormat InferFormatFromLibrary(string? libraryName)
    {
        if (string.IsNullOrWhiteSpace(libraryName))
        {
            return ModelFormat.SafeTensors;
        }

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
