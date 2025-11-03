using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Koan.Samples.Meridian.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Samples.Meridian.Services;

public interface IPdfRenderer
{
    Task<byte[]> RenderAsync(string markdown, CancellationToken ct = default);
}

public sealed class PandocPdfRenderer : IPdfRenderer
{
    private static readonly string[] BlockedLatexTokens =
    {
        "\\write18",
        "\\input",
        "\\include",
        "\\openout",
        "\\read",
        "\\catcode"
    };

    private readonly HttpClient _client;
    private readonly MeridianOptions _options;
    private readonly ILogger<PandocPdfRenderer> _logger;

    public PandocPdfRenderer(HttpClient client, IOptions<MeridianOptions> options, ILogger<PandocPdfRenderer> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<byte[]> RenderAsync(string markdown, CancellationToken ct = default)
    {
        var pandoc = _options.Rendering.Pandoc;
        if (!pandoc.Enabled)
        {
            return Array.Empty<byte>();
        }

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Array.Empty<byte>();
        }

        var sanitized = pandoc.SanitizeLatex ? SanitizeMarkdown(markdown) : markdown;
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return Array.Empty<byte>();
        }

        try
        {
            var endpoint = string.IsNullOrWhiteSpace(pandoc.Endpoint)
                ? "render"
                : pandoc.Endpoint.TrimStart('/');

            var request = new PandocRenderRequest(sanitized, ComputeHash(sanitized));
            var response = await _client.PostAsJsonAsync(endpoint, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Pandoc renderer returned {StatusCode} for endpoint {Endpoint}: {Error}",
                    response.StatusCode,
                    endpoint,
                    error);
                return Array.Empty<byte>();
            }

            var payload = await response.Content
                .ReadFromJsonAsync<PandocRenderResponse>(cancellationToken: ct)
                ;

            if (payload?.PdfBase64 is null)
            {
                _logger.LogWarning("Pandoc renderer response missing PDF payload.");
                return Array.Empty<byte>();
            }

            return Convert.FromBase64String(payload.PdfBase64);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pandoc renderer failed to convert markdown to PDF.");
            return Array.Empty<byte>();
        }
    }

    private static string SanitizeMarkdown(string markdown)
    {
        using var reader = new StringReader(markdown);
        var builder = new StringBuilder();
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            if (BlockedLatexTokens.Any(token => line.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed record PandocRenderRequest(
        [property: JsonPropertyName("markdown")] string Markdown,
        [property: JsonPropertyName("contentHash")] string ContentHash);

    private sealed record PandocRenderResponse(
        [property: JsonPropertyName("pdfBase64")] string? PdfBase64,
        [property: JsonPropertyName("error")] string? Error);
}
