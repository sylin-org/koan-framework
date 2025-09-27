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

        profile = await profile.Save(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.SampleText))
        {
            await UpsertEmbeddingAsync(profile, request.SampleText, cancellationToken).ConfigureAwait(false);
        }

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
        var suggestions = new List<DocumentProfileSuggestion>();

        if (!Vector<SemanticTypeEmbedding>.IsAvailable)
        {
            _logger.LogDebug("Vector adapter unavailable; skipping template suggestions.");
            return suggestions;
        }

        var documentVector = await BuildDocumentVectorAsync(chunks, cancellationToken).ConfigureAwait(false);
        if (documentVector is null)
        {
            return suggestions;
        }

        var profiles = await SemanticTypeProfile.All(cancellationToken).ConfigureAwait(false);
        var embeddings = await SemanticTypeEmbedding.All(cancellationToken).ConfigureAwait(false);
        var embeddingByProfile = embeddings
            .Where(e => e.Embedding is { Length: > 0 })
            .ToDictionary(e => e.SemanticTypeProfileId, e => e.Embedding);

        foreach (var profile in profiles)
        {
            if (profile.Archived)
            {
                continue;
            }

            if (!Guid.TryParse(profile.Id, out var profileId))
            {
                continue;
            }

            if (!embeddingByProfile.TryGetValue(profileId, out var embedding))
            {
                continue;
            }

            var similarity = CosineSimilarity(documentVector, embedding);
            if (similarity <= 0)
            {
                continue;
            }

            var confidence = CalibrateConfidence(similarity);
            var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["similarity"] = similarity.ToString("0.000", CultureInfo.InvariantCulture),
                ["confidence"] = confidence.ToString("0.000", CultureInfo.InvariantCulture),
                ["embeddingModel"] = _options.Ai.EmbeddingModel
            };

            if (!string.IsNullOrWhiteSpace(profile.Category))
            {
                diagnostics["category"] = profile.Category;
            }

            suggestions.Add(new DocumentProfileSuggestion
            {
                ProfileId = profile.Id,
                Confidence = confidence,
                Summary = profile.Description,
                SuggestedAt = DateTimeOffset.UtcNow,
                AutoAccepted = confidence >= 0.85,
                Diagnostics = diagnostics
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

    private async Task<float[]?> BuildDocumentVectorAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks is null || chunks.Count == 0)
        {
            return null;
        }

        var combined = string.Join(Environment.NewLine + Environment.NewLine, chunks.Select(c => c.Text));
        return await _embeddingGenerator.GenerateAsync(combined, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertEmbeddingAsync(SemanticTypeProfile profile, string sampleText, CancellationToken cancellationToken)
    {
        if (!Vector<SemanticTypeEmbedding>.IsAvailable)
        {
            return;
        }

        var embedding = await _embeddingGenerator.GenerateAsync(sampleText, cancellationToken).ConfigureAwait(false);
        if (embedding is null || embedding.Length == 0)
        {
            return;
        }

        if (!Guid.TryParse(profile.Id, out var profileId))
        {
            return;
        }

        var entity = new SemanticTypeEmbedding
        {
            SemanticTypeProfileId = profileId,
            Embedding = embedding,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        await entity.Save(cancellationToken).ConfigureAwait(false);
    }

    private static string GenerateCode(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"profile-{Guid.NewGuid():N}";
        }

        var span = name.Trim().ToLowerInvariant().AsSpan();
        Span<char> buffer = stackalloc char[Math.Min(span.Length, 60)];
        var index = 0;
        foreach (var ch in span)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = ch;
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_')
            {
                if (index > 0 && buffer[index - 1] != '-')
                {
                    buffer[index++] = '-';
                }
            }

            if (index >= buffer.Length)
            {
                break;
            }
        }

        if (index == 0)
        {
            return $"profile-{Guid.NewGuid():N}";
        }

        var code = new string(buffer[..index]).Trim('-');
        return string.IsNullOrWhiteSpace(code) ? $"profile-{Guid.NewGuid():N}" : code;
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

    private static double CalibrateConfidence(double similarity)
    {
        var normalized = (similarity + 1) / 2; // map -1..1 -> 0..1
        var adjusted = Math.Pow(Math.Clamp(normalized, 0, 1), 1.2); // emphasize higher matches
        return Math.Round(adjusted, 3, MidpointRounding.AwayFromZero);
    }
}

public sealed class DocumentProfileSuggestion
{
    public string ProfileId { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset SuggestedAt { get; set; }
    public bool AutoAccepted { get; set; }
    public Dictionary<string, string> Diagnostics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
