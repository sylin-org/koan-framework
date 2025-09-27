using Microsoft.Extensions.Logging;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public record VisionObservation(string Label, double Confidence, IDictionary<string, object>? Metadata = null);

public record VisionInsightResult(
    string Narrative,
    IReadOnlyList<VisionObservation> Observations,
    IDictionary<string, double> FieldHints,
    IDictionary<string, object> Diagnostics)
{
    public static VisionInsightResult Empty => new(
        Narrative: string.Empty,
        Observations: Array.Empty<VisionObservation>(),
        FieldHints: new Dictionary<string, double>(),
        Diagnostics: new Dictionary<string, object>());
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

    public VisionInsightService(ILogger<VisionInsightService> logger)
    {
        _logger = logger;
    }

    public async Task<VisionInsightResult?> TryExtractAsync(SourceDocument document, CancellationToken ct = default)
    {
        if (!ShouldProcess(document))
        {
            return null;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(document.StorageObjectKey) || !System.IO.File.Exists(document.StorageObjectKey))
            {
                return null;
            }

            await using var stream = System.IO.File.OpenRead(document.StorageObjectKey);
            using var image = await SixLabors.ImageSharp.Image.LoadAsync(stream, ct);

            var diagnostics = new Dictionary<string, object>
            {
                ["width"] = image.Width,
                ["height"] = image.Height,
                ["pixelFormat"] = image.PixelType.BitsPerPixel,
                ["sizeBytes"] = document.FileSizeBytes
            };

            var observations = new List<VisionObservation>
            {
                new("document", 0.6, new Dictionary<string, object>
                {
                    ["aspectRatio"] = Math.Round(image.Width / (double)image.Height, 2)
                })
            };

            var fieldHints = new Dictionary<string, double>
            {
                ["layout.visualDensity"] = Math.Clamp(image.Width * image.Height / 1_000_000d, 0, 1)
            };

            return new VisionInsightResult(
                Narrative: $"Vision scan completed for {document.DisplayName ?? document.FileName} ({image.Width}x{image.Height}).",
                Observations: observations,
                FieldHints: fieldHints,
                Diagnostics: diagnostics);
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

        var fileName = document.StorageObjectKey ?? document.FileName;
        var extension = System.IO.Path.GetExtension(fileName);
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
    }
}
