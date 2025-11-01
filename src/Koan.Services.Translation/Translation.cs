using Koan.Core.Hosting.App;
using Koan.Services.Abstractions;
using Koan.Services.Execution;
using Koan.Services.Translation.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Services.Translation;

/// <summary>
/// Static translation API matching Koan's Entity-first patterns.
/// Routes to in-process or remote translation service instances.
/// </summary>
public static class Translation
{
    private static ServiceExecutor<TranslationService> Executor
        => AppHost.Current?.GetService<ServiceExecutor<TranslationService>>()
           ?? throw new InvalidOperationException(
               "Translation service not registered. Ensure Koan Services is initialized via services.AddKoan().");

    /// <summary>
    /// Translate text to target language.
    /// </summary>
    /// <param name="text">Text to translate.</param>
    /// <param name="targetLanguage">Target language code (e.g., "es", "fr", "de").</param>
    /// <param name="sourceLanguage">Source language code (optional, "auto" = auto-detect).</param>
    /// <param name="model">AI model to use (optional).</param>
    /// <param name="policy">Load balancing policy for remote instances.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Translation result with detected source language and translated text.</returns>
    public static Task<TranslationResult> Translate(
        string text,
        string targetLanguage,
        string sourceLanguage = "auto",
        string? model = null,
        LoadBalancingPolicy policy = LoadBalancingPolicy.RoundRobin,
        CancellationToken ct = default)
    {
        var options = new TranslationOptions
        {
            Text = text,
            TargetLanguage = targetLanguage,
            SourceLanguage = sourceLanguage,
            Model = model
        };

        return Executor.ExecuteAsync<TranslationResult>(
            "translate",
            options,
            policy,
            ct);
    }

    /// <summary>
    /// Translate text using TranslationOptions.
    /// </summary>
    public static Task<TranslationResult> Translate(
        TranslationOptions options,
        LoadBalancingPolicy policy = LoadBalancingPolicy.RoundRobin,
        CancellationToken ct = default)
    {
        return Executor.ExecuteAsync<TranslationResult>(
            "translate",
            options,
            policy,
            ct);
    }

    /// <summary>
    /// Detect the language of text.
    /// </summary>
    /// <param name="text">Text to analyze.</param>
    /// <param name="policy">Load balancing policy for remote instances.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Language detection result with ISO 639-1 code.</returns>
    public static Task<LanguageDetectionResult> DetectLanguage(
        string text,
        LoadBalancingPolicy policy = LoadBalancingPolicy.RoundRobin,
        CancellationToken ct = default)
    {
        return Executor.ExecuteAsync<LanguageDetectionResult>(
            "detect-language",
            text,
            policy,
            ct);
    }
}
