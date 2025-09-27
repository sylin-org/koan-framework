using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Contracts;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed class TemplateSuggestionService : ITemplateSuggestionService
{
    private readonly IAi? _ai;
    private readonly DocMindOptions _options;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ILogger<TemplateSuggestionService> _logger;

    public TemplateSuggestionService(IServiceProvider serviceProvider, IOptions<DocMindOptions> options, IEmbeddingGenerator embeddingGenerator, ILogger<TemplateSuggestionService> logger)
    {
        _ai = serviceProvider.GetService<IAi>();
        _options = options.Value;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public async Task<SemanticTypeProfile> GenerateAsync(TemplateGenerationRequest request, CancellationToken cancellationToken)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var profile = new SemanticTypeProfile
        {
            Name = request.Name,
            Description = request.Description,
            Metadata = request.Metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        if (_ai is not null)
        {
            try
            {
                var prompt = BuildTemplatePrompt(request);
                var response = await _ai.PromptAsync(new AiChatRequest
                {
                    Model = _options.Ai.DefaultModel,
                    Messages =
                    {
                        new AiMessage("system", "Design a JSON extraction template."),
                        new AiMessage("user", prompt)
                    }
                }, cancellationToken).ConfigureAwait(false);

                profile.Prompt = ParsePrompt(response.Text, request);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Template generation fallback used for {Name}", request.Name);
                profile.Prompt = BuildFallbackPrompt(request);
            }
        }
        else
        {
            profile.Prompt = BuildFallbackPrompt(request);
        }

        if (!string.IsNullOrWhiteSpace(request.SampleText))
        {
            profile.Embedding = await _embeddingGenerator.GenerateAsync(request.SampleText, cancellationToken).ConfigureAwait(false);
        }

        profile = await profile.Save(cancellationToken);
        return profile;
    }

    public async Task<TemplatePromptTestResult> RunPromptTestAsync(SemanticTypeProfile profile, TemplatePromptTestRequest request, CancellationToken cancellationToken)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (_ai is null)
        {
            return new TemplatePromptTestResult
            {
                RawResponse = "AI provider not configured."
            };
        }

        var userPrompt = profile.Prompt.UserTemplate.Replace("{{text}}", request.Text, StringComparison.OrdinalIgnoreCase);
        if (request.Variables is not null)
        {
            foreach (var (key, value) in request.Variables)
            {
                userPrompt = userPrompt.Replace("{{" + key + "}}", value, StringComparison.OrdinalIgnoreCase);
            }
        }

        var response = await _ai.PromptAsync(new AiChatRequest
        {
            Model = _options.Ai.DefaultModel,
            Messages =
            {
                new AiMessage("system", profile.Prompt.SystemPrompt),
                new AiMessage("user", userPrompt)
            }
        }, cancellationToken).ConfigureAwait(false);

        return new TemplatePromptTestResult
        {
            RawResponse = response.Text,
            Diagnostics = new Dictionary<string, string>
            {
                ["model"] = response.Model ?? _options.Ai.DefaultModel,
                ["tokensIn"] = response.TokensIn?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["tokensOut"] = response.TokensOut?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            }
        };
    }

    public async Task<IReadOnlyList<DocumentProfileSuggestion>> SuggestAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var profiles = await SemanticTypeProfile.All(cancellationToken).ConfigureAwait(false);
        var suggestions = new List<DocumentProfileSuggestion>();
        var docVector = AverageEmbedding(chunks);
        if (docVector is null)
        {
            return suggestions;
        }

        foreach (var profile in profiles)
        {
            if (profile.Archived || profile.Embedding is null) continue;
            var similarity = CosineSimilarity(docVector, profile.Embedding);
            if (similarity <= 0) continue;
            suggestions.Add(new DocumentProfileSuggestion
            {
                ProfileId = profile.Id,
                Confidence = similarity,
                Summary = profile.Description,
                SuggestedAt = DateTimeOffset.UtcNow,
                AutoAccepted = false
            });
        }

        return suggestions
            .OrderByDescending(s => s.Confidence)
            .Take(5)
            .ToList();
    }

    private static PromptTemplate BuildFallbackPrompt(TemplateGenerationRequest request)
        => new()
        {
            SystemPrompt = "Extract structured information from the supplied document.",
            UserTemplate = "Summarise key highlights from the document: {{text}}",
            Variables = new Dictionary<string, string>
            {
                ["sample"] = request.SampleText ?? string.Empty
            }
        };

    private static PromptTemplate ParsePrompt(string response, TemplateGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return BuildFallbackPrompt(request);
        }

        try
        {
            var parts = response.Split("---", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return new PromptTemplate
                {
                    SystemPrompt = parts[0].Trim(),
                    UserTemplate = parts[1].Trim(),
                    Variables = new Dictionary<string, string>
                    {
                        ["sample"] = request.SampleText ?? string.Empty
                    }
                };
            }
        }
        catch
        {
            // Ignore and fallback
        }

        return BuildFallbackPrompt(request);
    }

    private static string BuildTemplatePrompt(TemplateGenerationRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Create a structured extraction template for the following document description:");
        builder.AppendLine(request.Description);
        if (!string.IsNullOrWhiteSpace(request.SampleText))
        {
            builder.AppendLine("Sample text:");
            builder.AppendLine(request.SampleText.Length > 2000 ? request.SampleText[..2000] : request.SampleText);
        }
        builder.AppendLine("Respond with two sections separated by '---'. First line: system prompt, second section: user template referencing {{text}}.");
        return builder.ToString();
    }

    private static float[]? AverageEmbedding(IReadOnlyList<DocumentChunk> chunks)
    {
        var vectors = chunks
            .Select(c => c.Embedding)
            .Where(e => e is not null)
            .Cast<float[]>()
            .ToList();

        if (vectors.Count == 0) return null;

        var dimension = vectors[0].Length;
        var accumulator = new double[dimension];
        foreach (var vector in vectors)
        {
            for (var i = 0; i < dimension; i++)
            {
                accumulator[i] += vector[i];
            }
        }

        var avg = new float[dimension];
        for (var i = 0; i < dimension; i++)
        {
            avg[i] = (float)(accumulator[i] / vectors.Count);
        }
        return avg;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
