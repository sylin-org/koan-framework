using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed class InsightSynthesisService : IInsightSynthesisService
{
    private readonly IAi? _ai;
    private readonly DocMindAiOptions _aiOptions;
    private readonly ILogger<InsightSynthesisService> _logger;

    public InsightSynthesisService(IServiceProvider serviceProvider, IOptions<DocMindAiOptions> aiOptions, ILogger<InsightSynthesisService> logger)
    {
        _ai = serviceProvider.GetService<IAi>();
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentInsight>> GenerateAsync(SourceDocument document, DocumentExtractionResult extraction, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var insights = new List<DocumentInsight>();

        if (_ai is not null && !string.IsNullOrWhiteSpace(extraction.Text))
        {
            try
            {
                var prompt = BuildPrompt(document, extraction);
                var response = await _ai.PromptAsync(new AiChatRequest
                {
                    Model = _aiOptions.DefaultModel,
                    Messages =
                    {
                        new AiMessage("system", "You are DocMind, an analyst producing structured findings."),
                        new AiMessage("user", prompt)
                    }
                }, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    insights.Add(new DocumentInsight
                    {
                        DocumentId = document.Id,
                        Title = "Executive summary",
                        Content = response.Text.Trim(),
                        Confidence = 0.7,
                        Channel = DocumentChannels.Text,
                        Metadata = new Dictionary<string, string>
                        {
                            ["model"] = response.Model ?? _aiOptions.DefaultModel,
                            ["tokensIn"] = response.TokensIn?.ToString(CultureInfo.InvariantCulture) ?? "",
                            ["tokensOut"] = response.TokensOut?.ToString(CultureInfo.InvariantCulture) ?? ""
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI insight synthesis failed for document {DocumentId}", document.Id);
            }
        }

        if (insights.Count == 0)
        {
            var sb = new StringBuilder();
            foreach (var chunk in chunks.Take(3))
            {
                sb.AppendLine(chunk.Summary ?? chunk.Content[..Math.Min(chunk.Content.Length, 160)]);
                sb.AppendLine();
            }

            var fallback = sb.Length == 0 ? extraction.Text[..Math.Min(extraction.Text.Length, 320)] : sb.ToString();
            insights.Add(new DocumentInsight
            {
                DocumentId = document.Id,
                Title = "Auto-generated overview",
                Content = fallback.Trim(),
                Confidence = 0.4,
                Channel = DocumentChannels.Text
            });
        }

        var index = 0;
        foreach (var chunk in chunks)
        {
            insights.Add(new DocumentInsight
            {
                DocumentId = document.Id,
                ChunkId = chunk.Id,
                Channel = chunk.Channel,
                Title = $"Chunk {chunk.Index + 1} highlight",
                Content = chunk.Summary ?? chunk.Content,
                Confidence = 0.3,
                Metadata = new Dictionary<string, string>
                {
                    ["chunkIndex"] = chunk.Index.ToString(CultureInfo.InvariantCulture)
                }
            });
            index++;
        }

        return insights;
    }

    private static string BuildPrompt(SourceDocument document, DocumentExtractionResult extraction)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Document: {document.OriginalFileName}");
        if (!string.IsNullOrWhiteSpace(document.AssignedProfileId))
        {
            builder.AppendLine($"Assigned profile: {document.AssignedProfileId}");
        }
        builder.AppendLine("---");
        builder.AppendLine(extraction.Text.Length > 4000 ? extraction.Text[..4000] : extraction.Text);
        builder.AppendLine("---");
        builder.AppendLine("Summarise the key risks, commitments, and dates in bullet form.");
        return builder.ToString();
    }
}
