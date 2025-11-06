using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;

namespace S7.Meridian.Tests.Fakes;

/// <summary>
/// Deterministic AI stub for authoring endpoints so tests do not require external LLMs.
/// Generates stable JSON payloads that satisfy SourceType and AnalysisType authoring contracts.
/// </summary>
internal sealed class FakeAuthoringAi : IAi
{
    public Task<AiChatResponse> PromptAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var prompt = request.Messages.LastOrDefault()?.Content ?? string.Empty;
        var text = prompt.Contains("\"fieldQueries\"", StringComparison.OrdinalIgnoreCase)
            ? BuildSourceTypeDraft()
            : BuildAnalysisTypeDraft();

        return Task.FromResult(new AiChatResponse
        {
            Text = text,
            Model = request.Model ?? "fake-authoring",
            TokensOut = text.Length / 8
        });
    }

    public async IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await PromptAsync(request, ct).ConfigureAwait(false);
        yield return new AiChatChunk
        {
            DeltaText = response.Text,
            Index = 0,
            Model = response.Model
        };
    }

    public Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var vectors = request.Input.Select(ComputeVector).ToList();
        return Task.FromResult(new AiEmbeddingsResponse
        {
            Vectors = vectors,
            Model = request.Model ?? "fake-embedding",
            Dimension = vectors.Count > 0 ? vectors[0].Length : 0
        });
    }

    public async Task<string> PromptAsync(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
    {
        var response = await PromptAsync(new AiChatRequest
        {
            Model = model,
            Options = opts,
            Messages = new List<AiMessage> { new("user", message) }
        }, ct).ConfigureAwait(false);

        return response.Text;
    }

    public async IAsyncEnumerable<AiChatChunk> StreamAsync(string message, string? model = null, AiPromptOptions? opts = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new AiChatRequest
        {
            Model = model,
            Options = opts,
            Messages = new List<AiMessage> { new("user", message) }
        };

        await foreach (var chunk in StreamAsync(request, ct))
        {
            yield return chunk;
        }
    }

    private static string BuildSourceTypeDraft() =>
        """
        {
          "name": "Vendor Prescreen Questionnaire",
          "description": "Structured intake covering vendor finances and compliance evidence.",
          "tags": ["finance", "compliance", "architecture"],
          "descriptorHints": ["questionnaire", "security posture", "financial summary"],
          "signalPhrases": ["SOC 2", "FedRAMP", "annual revenue"],
          "supportsManualSelection": true,
          "mimeTypes": ["text/plain", "application/pdf"],
          "expectedPageCount": { "min": 2, "max": 12 },
          "fieldQueries": {
            "$.annualRevenue": "annual revenue",
            "$.headcount": "headcount",
            "$.complianceCertifications": "compliance certification"
          },
          "instructions": "Capture revenue, workforce scale, and compliance posture from prescreen responses. Prioritize numbers grounded in the attached questionnaire and flag any missing disclosures.",
          "outputTemplate": "## Vendor Snapshot\n- **Revenue**: {{annual_revenue}}\n- **Headcount**: {{headcount}}\n- **Compliance Certifications**: {{compliance_certifications}}"
        }
        """;

    private static string BuildAnalysisTypeDraft() =>
        """
        {
          "name": "Enterprise Architecture Review",
          "description": "Summarizes solution posture, risks, and recommended actions for executive review.",
          "tags": ["architecture", "strategy", "governance"],
          "descriptors": ["executive_summary", "risk_profile", "recommendations"],
          "instructions": "As the lead enterprise architect, synthesize the provided findings into a concise narrative for the CIO steering committee. Highlight integration implications, security posture, and roadmap alignment. Provide actionable recommendations with ownership and timeline guidance.",
          "outputFields": [
            "executive_summary",
            "strengths",
            "risks",
            "recommendations",
            "next_steps"
          ],
          "outputTemplate": "## Executive Summary\n{{executive_summary}}\n\n## Strengths\n{{strengths}}\n\n## Risks\n{{risks}}\n\n## Recommendations\n{{recommendations}}\n\n## Next Steps\n{{next_steps}}"
        }
        """;

    private static float[] ComputeVector(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vector = new float[8];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = bytes[i] / 255f;
        }
        return vector;
    }
}
