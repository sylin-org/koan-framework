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
    private readonly DocMindOptions _options;
    private readonly ILogger<InsightSynthesisService> _logger;

    public InsightSynthesisService(IServiceProvider serviceProvider, IOptions<DocMindOptions> options, ILogger<InsightSynthesisService> logger)
    {
        _ai = serviceProvider.GetService<IAi>();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentInsight>> GenerateAsync(SourceDocument document, DocumentExtractionResult extraction, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var insights = new List<DocumentInsight>();

        var documentId = Guid.Parse(document.Id);

        if (_ai is not null && !string.IsNullOrWhiteSpace(extraction.Text))
        {
            try
            {
                var prompt = BuildPrompt(document, extraction);
                var response = await _ai.PromptAsync(new AiChatRequest
                {
                    Model = _options.Ai.DefaultModel,
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
                        SourceDocumentId = documentId,
                        Channel = InsightChannel.Text,
                        Heading = "Executive summary",
                        Body = response.Text.Trim(),
                        Confidence = 0.7,
                        Metadata = new Dictionary<string, string>
                        {
                            ["model"] = response.Model ?? _options.Ai.DefaultModel,
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
                SourceDocumentId = documentId,
                Channel = InsightChannel.Text,
                Heading = "Auto-generated overview",
                Body = fallback.Trim(),
                Confidence = 0.4
            });
        }

        foreach (var chunk in chunks)
        {
            var chunkId = Guid.Parse(chunk.Id);
            insights.Add(new DocumentInsight
            {
                SourceDocumentId = documentId,
                ChunkId = chunkId,
                Channel = InsightChannel.Text,
                Heading = $"Chunk {chunk.Order + 1} highlight",
                Body = chunk.Text.Length > 200 ? chunk.Text[..200] + "…" : chunk.Text,
                Confidence = 0.3
            });
        }

        return insights;
    }

    private static string BuildPrompt(SourceDocument document, DocumentExtractionResult extraction)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Document: {document.DisplayName ?? document.FileName}");
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
