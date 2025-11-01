using Koan.Services.Abstractions;
using Koan.Services.Execution;
using Koan.Services.Translation;
using Koan.Services.Translation.Models;
using Microsoft.AspNetCore.Mvc;

namespace S8.PolyglotShop.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranslationController : ControllerBase
{
    private readonly ServiceExecutor<TranslationService> _executor;

    public TranslationController(ServiceExecutor<TranslationService> executor)
    {
        _executor = executor;
    }

    /// <summary>
    /// Translate text to target language.
    /// </summary>
    [HttpPost("translate")]
    public async Task<IActionResult> Translate(
        [FromBody] TranslateRequest request,
        CancellationToken ct)
    {
        // Basic input validation only - service handles business rules
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text is required" });

        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            return BadRequest(new { error = "TargetLanguage is required" });

        var options = new TranslationOptions
        {
            Text = request.Text,
            TargetLanguage = request.TargetLanguage,
            SourceLanguage = request.SourceLanguage // Service normalizes null/empty to "auto"
        };

        // ServiceExecutor handles routing and load balancing
        var result = await _executor.ExecuteAsync<TranslationResult>(
            "translate",
            options,
            ct: ct);

        return Ok(result);
    }

    /// <summary>
    /// Detect the language of text.
    /// </summary>
    [HttpPost("detect")]
    public async Task<IActionResult> DetectLanguage(
        [FromBody] DetectRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text is required" });

        var result = await _executor.ExecuteAsync<LanguageDetectionResult>(
            "detect-language",
            request.Text,
            ct: ct);

        return Ok(result);
    }

    /// <summary>
    /// Get supported languages from Translation service.
    /// </summary>
    [HttpGet("languages")]
    public async Task<IActionResult> GetLanguages(CancellationToken ct)
    {
        var languages = await _executor.ExecuteAsync<SupportedLanguage[]>(
            "get-languages",
            parameters: null,
            ct: ct);

        return Ok(languages);
    }
}

// Request models
public record TranslateRequest(string Text, string TargetLanguage, string? SourceLanguage = null);
public record DetectRequest(string Text);
