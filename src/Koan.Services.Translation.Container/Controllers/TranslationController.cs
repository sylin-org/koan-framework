using Koan.Services.Translation;
using Koan.Services.Translation.Models;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Services.Translation.Container.Controllers;

/// <summary>
/// HTTP API controller for translation service.
/// Exposes service capabilities to external consumers.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TranslationController : ControllerBase
{
    private readonly TranslationService _translationService;
    private readonly ILogger<TranslationController> _logger;

    public TranslationController(
        TranslationService translationService,
        ILogger<TranslationController> logger)
    {
        _translationService = translationService;
        _logger = logger;
    }

    /// <summary>
    /// Translate text to target language.
    /// POST /api/translation/translate
    /// </summary>
    [HttpPost("translate")]
    public async Task<ActionResult<TranslationResult>> Translate(
        [FromBody] TranslationOptions options,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation(
                "Translation request: {SourceLang} → {TargetLang}",
                options.SourceLanguage,
                options.TargetLanguage);

            var result = await _translationService.Translate(options, ct);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Detect language of text.
    /// POST /api/translation/detect-language
    /// </summary>
    [HttpPost("detect-language")]
    public async Task<ActionResult<LanguageDetectionResult>> DetectLanguage(
        [FromBody] DetectLanguageRequest request,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Language detection request for {Length} chars", request.Text.Length);

            var result = await _translationService.DetectLanguage(request.Text, ct);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Language detection failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

/// <summary>
/// Request model for language detection endpoint.
/// </summary>
public class DetectLanguageRequest
{
    public string Text { get; set; } = "";
}
