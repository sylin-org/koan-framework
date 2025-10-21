using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Samples.Meridian.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Samples.Meridian.Services;

public interface IOcrClient
{
    Task<OcrExtractionResult?> ExtractAsync(Stream pdfStream, CancellationToken ct);
}

public sealed record OcrExtractionResult(string Text, double Confidence, int PageCount);

public sealed class TesseractOcrClient : IOcrClient
{
    private readonly HttpClient _httpClient;
    private readonly MeridianOptions _options;
    private readonly ILogger<TesseractOcrClient> _logger;

    public TesseractOcrClient(HttpClient httpClient, IOptions<MeridianOptions> options, ILogger<TesseractOcrClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OcrExtractionResult?> ExtractAsync(Stream pdfStream, CancellationToken ct)
    {
        var ocrOptions = _options.Extraction.Ocr;
        if (!ocrOptions.Enabled)
        {
            return null;
        }

        if (pdfStream.CanSeek)
        {
            pdfStream.Position = 0;
        }

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(pdfStream, 81920);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(streamContent, "file", "document.pdf");

        var endpoint = string.IsNullOrWhiteSpace(ocrOptions.Endpoint)
            ? "ocr"
            : ocrOptions.Endpoint.TrimStart('/');

        try
        {
            using var response = await _httpClient.PostAsync(endpoint, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Tesseract OCR request failed with status {StatusCode}.", response.StatusCode);
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<OcrResponse>(responseStream, cancellationToken: ct)
                .ConfigureAwait(false);

            if (payload is null || string.IsNullOrWhiteSpace(payload.Text))
            {
                _logger.LogWarning("Tesseract OCR response payload was empty.");
                return null;
            }

            var confidence = payload.Confidence ?? ocrOptions.ConfidenceFloor;
            var pageCount = payload.Pages ?? 0;

            return new OcrExtractionResult(payload.Text, confidence, pageCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tesseract OCR request failed.");
            return null;
        }
    }

    private sealed record OcrResponse(string? Text, double? Confidence, int? Pages);
}
