using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts.Models;
using Koan.Data.Vector;
using Microsoft.Extensions.Logging;
using S12.MedTrials.Contracts;
using S12.MedTrials.Models;

namespace S12.MedTrials.Services;

public sealed class ProtocolDocumentService : IProtocolDocumentService
{
    private readonly ILogger<ProtocolDocumentService>? _logger;

    public ProtocolDocumentService(ILogger<ProtocolDocumentService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ProtocolDocumentIngestionResult> IngestAsync(ProtocolDocumentIngestionRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("Content is required", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var document = new ProtocolDocument
        {
            TrialSiteId = string.IsNullOrWhiteSpace(request.TrialSiteId) ? null : request.TrialSiteId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled Document" : request.Title,
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? "Protocol" : request.DocumentType,
            Version = request.Version,
            ExtractedText = request.Content.Trim(),
            Tags = request.Tags ?? Array.Empty<string>(),
            EffectiveDate = request.EffectiveDate ?? now,
            IngestedAt = now
        };

        if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            document.Metadata["sourceUrl"] = request.SourceUrl!;
        }

        var warnings = new List<string>();
        bool vectorized = false;
        bool degraded = false;
        string? model = null;

        var ai = Ai.TryResolve();
        if (ai is not null)
        {
            try
            {
                var payload = BuildEmbeddingPayload(document);
                var embeddingResponse = await ai.EmbedAsync(new AiEmbeddingsRequest
                {
                    Input = { payload }
                }, ct).ConfigureAwait(false);

                if (embeddingResponse.Vectors.Count > 0 && Vector<ProtocolDocument>.IsAvailable)
                {
                    var vector = embeddingResponse.Vectors[0];
                    await Vector<ProtocolDocument>.Save((document.Id, vector, new
                    {
                        document.Id,
                        document.Title,
                        document.DocumentType,
                        document.TrialSiteId,
                        document.Tags
                    }), ct).ConfigureAwait(false);
                    document.LastEmbeddedAt = now;
                    document.VectorState = ProtocolVectorState.Indexed;
                    vectorized = true;
                    model = embeddingResponse.Model;
                }
                else
                {
                    degraded = true;
                    document.VectorState = ProtocolVectorState.Degraded;
                    warnings.Add(Vector<ProtocolDocument>.IsAvailable
                        ? "Embedding provider returned no vectors. Stored document without vector search."
                        : "Vector store unavailable; stored document without embeddings.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                degraded = true;
                document.VectorState = ProtocolVectorState.Degraded;
                warnings.Add("Failed to generate embeddings. Stored document for deterministic lookup only.");
                _logger?.LogWarning(ex, "Protocol ingestion embedding failed for {Title}", document.Title);
            }
        }
        else
        {
            degraded = true;
            document.VectorState = ProtocolVectorState.Degraded;
            warnings.Add("AI provider unavailable; stored document without embeddings.");
        }

        if (warnings.Count > 0)
        {
            foreach (var warning in warnings)
            {
                document.Diagnostics.Add(new DocumentDiagnostic
                {
                    Code = "ingest-warning",
                    Message = warning,
                    Severity = DiagnosticSeverity.Warning,
                    RecordedAt = now
                });
            }
        }

        await ProtocolDocument.UpsertMany(new[] { document }, ct).ConfigureAwait(false);

        return new ProtocolDocumentIngestionResult(
            document,
            vectorized,
            degraded,
            warnings,
            model,
            document.Diagnostics);
    }

    public async Task<ProtocolDocumentQueryResult> QueryAsync(ProtocolDocumentQueryRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new ProtocolDocumentQueryResult(Array.Empty<ProtocolDocumentMatch>(), true, null, new[] { "Query text is required." });
        }

        var matches = new List<ProtocolDocumentMatch>();
        var warnings = new List<string>();
        bool degraded = false;
        string? model = null;

        var ai = Ai.TryResolve();
        if (ai is not null && Vector<ProtocolDocument>.IsAvailable)
        {
            try
            {
                var response = await ai.EmbedAsync(new AiEmbeddingsRequest
                {
                    Input = { request.Query }
                }, ct).ConfigureAwait(false);

                if (response.Vectors.Count > 0)
                {
                    var vectorResult = await Vector<ProtocolDocument>.Search(response.Vectors[0], topK: request.TopK <= 0 ? 5 : Math.Min(request.TopK, 20), ct: ct).ConfigureAwait(false);
                    foreach (var match in vectorResult.Matches)
                    {
                        var doc = await ProtocolDocument.Get(match.Id, ct).ConfigureAwait(false);
                        if (doc is null) continue;
                        if (!string.IsNullOrWhiteSpace(request.TrialSiteId) && !string.Equals(doc.TrialSiteId, request.TrialSiteId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        matches.Add(new ProtocolDocumentMatch(
                            doc.Id,
                            match.Score,
                            BuildSnippet(doc, request.Query, request.IncludeContent),
                            doc));
                    }

                    model = response.Model;
                }
                else
                {
                    degraded = true;
                    warnings.Add("Embedding provider returned no vectors. Falling back to deterministic search.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                degraded = true;
                warnings.Add("Vector search failed. Falling back to deterministic search.");
                _logger?.LogWarning(ex, "Vector query failed for '{Query}'", request.Query);
            }
        }
        else
        {
            degraded = true;
            if (ai is null)
            {
                warnings.Add("AI provider unavailable; using deterministic search.");
            }

            if (!Vector<ProtocolDocument>.IsAvailable)
            {
                warnings.Add("Vector store unavailable; using deterministic search.");
            }
        }

        if (matches.Count == 0)
        {
            var allDocs = await ProtocolDocument.All(ct).ConfigureAwait(false);
            var query = allDocs.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(request.TrialSiteId))
            {
                query = query.Where(d => d.TrialSiteId == request.TrialSiteId);
            }

            var deterministic = query.Where(d => d.ExtractedText.Contains(request.Query, StringComparison.OrdinalIgnoreCase)
                    || d.Title.Contains(request.Query, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var doc in deterministic)
            {
                if (matches.Any(m => m.DocumentId == doc.Id))
                {
                    continue;
                }

                matches.Add(new ProtocolDocumentMatch(
                    doc.Id,
                    0d,
                    BuildSnippet(doc, request.Query, request.IncludeContent),
                    doc));
            }
        }

        return new ProtocolDocumentQueryResult(matches, degraded, model, warnings);
    }

    private static string BuildEmbeddingPayload(ProtocolDocument document)
    {
        var tags = document.Tags is { Length: > 0 }
            ? $"Tags: {string.Join(", ", document.Tags)}"
            : string.Empty;

        return string.Join('\n', new[]
        {
            document.Title,
            $"Type: {document.DocumentType}",
            string.IsNullOrWhiteSpace(document.Version) ? null : $"Version: {document.Version}",
            tags,
            document.ExtractedText
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string? BuildSnippet(ProtocolDocument doc, string query, bool includeContent)
    {
        if (includeContent)
        {
            return doc.ExtractedText;
        }

        if (string.IsNullOrWhiteSpace(doc.ExtractedText))
        {
            return null;
        }

        var text = doc.ExtractedText;
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = Math.Min(text.Length / 2, text.Length);
        }

        var start = Math.Max(0, index - 120);
        var length = Math.Min(240, text.Length - start);
        var snippet = text.Substring(start, length).Trim();

        if (start > 0) snippet = "…" + snippet;
        if (start + length < text.Length) snippet += "…";

        return snippet;
    }
}
