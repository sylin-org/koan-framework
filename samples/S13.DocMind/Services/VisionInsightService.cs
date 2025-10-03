using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public record VisionObservation(string Label, double Confidence, string? Summary, IDictionary<string, object?> Metadata);

public record VisionInsightResult(
    string Narrative,
    IReadOnlyList<VisionObservation> Observations,
    IDictionary<string, double> FieldHints,
    IDictionary<string, object?> Diagnostics,
    IDictionary<string, object?> StructuredPayload,
    string? ExtractedText,
    double? Confidence,
    string? Model)
{
    public static VisionInsightResult Empty => new(
        Narrative: string.Empty,
        Observations: Array.Empty<VisionObservation>(),
        FieldHints: new Dictionary<string, double>(),
        Diagnostics: new Dictionary<string, object?>(),
        StructuredPayload: new Dictionary<string, object?>(),
        ExtractedText: null,
        Confidence: null,
        Model: null);
}

public interface IVisionInsightService
{
    Task<VisionInsightResult?> TryExtractAsync(SourceDocument document, CancellationToken ct = default);
}

public sealed class VisionInsightService : IVisionInsightService
{
    private static readonly HashSet<string> VisionContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/bmp",
        "image/webp"
    };

    private readonly ILogger<VisionInsightService> _logger;
    private readonly IAi? _ai;
    private readonly DocMindOptions _options;
    private readonly IDocumentStorage _storage;

    public VisionInsightService(IServiceProvider serviceProvider, IOptions<DocMindOptions> options, IDocumentStorage storage, ILogger<VisionInsightService> logger)
    {
        _storage = storage;
        _logger = logger;
        _ai = serviceProvider.GetService<IAi>();
        _options = options.Value;
    }

    public async Task<VisionInsightResult?> TryExtractAsync(SourceDocument document, CancellationToken ct = default)
    {
        if (!ShouldProcess(document))
        {
            return null;
        }

        try
        {
            await using var stream = await OpenStreamAsync(document, ct).ConfigureAwait(false);
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            buffer.Position = 0;
            using var image = await SixLabors.ImageSharp.Image.LoadAsync(buffer, ct).ConfigureAwait(false);

            var baseDiagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["width"] = image.Width,
                ["height"] = image.Height,
                ["pixelFormat"] = image.PixelType.BitsPerPixel,
                ["sizeBytes"] = document.FileSizeBytes,
                ["aspectRatio"] = Math.Round(image.Width / (double)image.Height, 2)
            };

            if (_ai is null)
            {
                return BuildFallbackResult(document, baseDiagnostics);
            }

            var model = _options.Ai.VisionModel ?? _options.Ai.DefaultModel;
            var request = new AiChatRequest
            {
                Model = model,
                Options = new AiPromptOptions
                {
                    Temperature = 0.2,
                    MaxOutputTokens = 1200
                },
                Messages =
                {
                    new AiMessage("system", "You are DocMind Vision. Return ONLY JSON with fields: narrative (string), extractedText (string), confidence (0-1), observations (array of { label, confidence, summary, metadata }), fieldHints (object of numeric scores), diagnostics (object), structured (object)."),
                    new AiMessage("user", $"Analyze this document image for insights. Respond with JSON only. base64://{document.ContentType}:{Convert.ToBase64String(bytes)}")
                }
            };

            var response = await _ai.PromptAsync(request, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response.Text))
            {
                return BuildFallbackResult(document, baseDiagnostics, "empty-response", model);
            }

            try
            {
                using var json = JsonDocument.Parse(response.Text);
                var root = json.RootElement;

                var narrative = root.TryGetProperty("narrative", out var narrativeElement)
                    ? narrativeElement.GetString() ?? string.Empty
                    : string.Empty;

                var extractedText = root.TryGetProperty("extractedText", out var textElement)
                    ? textElement.GetString()
                    : null;

                double? confidence = null;
                if (root.TryGetProperty("confidence", out var confidenceElement) &&
                    confidenceElement.TryGetDouble(out var conf))
                {
                    confidence = Math.Clamp(conf, 0, 1);
                }

                var observations = new List<VisionObservation>();
                if (root.TryGetProperty("observations", out var observationElement) && observationElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in observationElement.EnumerateArray())
                    {
                        if (!item.TryGetProperty("label", out var labelElement))
                        {
                            continue;
                        }

                        var label = labelElement.GetString();
                        if (string.IsNullOrWhiteSpace(label))
                        {
                            continue;
                        }

                        double obsConfidence = 0.5;
                        if (item.TryGetProperty("confidence", out var obsConfidenceElement) &&
                            obsConfidenceElement.TryGetDouble(out var obsConfidenceValue))
                        {
                            obsConfidence = Math.Clamp(obsConfidenceValue, 0, 1);
                        }

                        var summary = item.TryGetProperty("summary", out var summaryElement)
                            ? summaryElement.GetString()
                            : null;

                        var metadata = item.TryGetProperty("metadata", out var metadataElement) && metadataElement.ValueKind == JsonValueKind.Object
                            ? metadataElement.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value), StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, object?>();

                        observations.Add(new VisionObservation(label, obsConfidence, summary, metadata));
                    }
                }

                var fieldHints = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("fieldHints", out var hintsElement) && hintsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in hintsElement.EnumerateObject())
                    {
                        if (property.Value.TryGetDouble(out var hintValue))
                        {
                            fieldHints[property.Name] = Math.Clamp(hintValue, 0, 1);
                        }
                    }
                }

                var diagnostics = new Dictionary<string, object?>(baseDiagnostics, StringComparer.OrdinalIgnoreCase)
                {
                    ["model"] = response.Model ?? model,
                    ["tokensIn"] = response.TokensIn,
                    ["tokensOut"] = response.TokensOut
                };

                if (root.TryGetProperty("diagnostics", out var diagnosticsElement) && diagnosticsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in diagnosticsElement.EnumerateObject())
                    {
                        diagnostics[property.Name] = ConvertJsonValue(property.Value);
                    }
                }

                var structured = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("structured", out var structuredElement) && structuredElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in structuredElement.EnumerateObject())
                    {
                        structured[property.Name] = ConvertJsonValue(property.Value);
                    }
                }

                return new VisionInsightResult(
                    Narrative: string.IsNullOrWhiteSpace(narrative)
                        ? $"Vision scan completed for {document.DisplayName ?? document.FileName} ({image.Width}x{image.Height})."
                        : narrative.Trim(),
                    Observations: observations,
                    FieldHints: fieldHints,
                    Diagnostics: diagnostics,
                    StructuredPayload: structured,
                    ExtractedText: extractedText,
                    Confidence: confidence,
                    Model: response.Model ?? model);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Vision prompt returned invalid JSON for {DocumentId}", document.Id);
                return BuildFallbackResult(document, baseDiagnostics, "json-parse", model);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to run lightweight vision insight extraction for {DocumentId}", document.Id);
            return VisionInsightResult.Empty;
        }
    }

    private static bool ShouldProcess(SourceDocument document)
    {
        if (string.IsNullOrEmpty(document.ContentType))
        {
            return false;
        }

        if (VisionContentTypes.Contains(document.ContentType))
        {
            return true;
        }

        var fileName = document.Storage.ObjectKey ?? document.FileName;
        var extension = Path.GetExtension(fileName);
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff";
    }

    private async Task<Stream> OpenStreamAsync(SourceDocument document, CancellationToken ct)
    {
        if (document.Storage.TryResolvePhysicalPath(out var path) && File.Exists(path))
        {
            return File.OpenRead(path);
        }

        return await _storage.OpenReadAsync(document.Storage, ct).ConfigureAwait(false);
    }

    private static VisionInsightResult BuildFallbackResult(SourceDocument document, IDictionary<string, object?> diagnostics, string? reason = null, string? model = null)
    {
        var narrative = $"Vision scan completed for {document.DisplayName ?? document.FileName}.";
        if (!string.IsNullOrWhiteSpace(reason))
        {
            diagnostics = new Dictionary<string, object?>(diagnostics, StringComparer.OrdinalIgnoreCase)
            {
                ["fallbackReason"] = reason
            };
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            diagnostics["model"] = model;
        }

        var observations = new List<VisionObservation>
        {
            new("document", 0.4, "Basic visual analysis placeholder", new Dictionary<string, object?>())
        };

        var fieldHints = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["vision.available"] = 0
        };

        return new VisionInsightResult(
            Narrative: narrative,
            Observations: observations,
            FieldHints: fieldHints,
            Diagnostics: new Dictionary<string, object?>(diagnostics),
            StructuredPayload: new Dictionary<string, object?>(),
            ExtractedText: null,
            Confidence: null,
            Model: model);
    }

    private static object? ConvertJsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value), StringComparer.OrdinalIgnoreCase),
            _ => element.ToString()
        };
}
