using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Contracts;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IManualAnalysisService
{
    Task<ManualAnalysisRunResponse?> RunSessionAsync(string sessionId, ManualAnalysisRunRequest? request, CancellationToken cancellationToken);
    Task<ManualAnalysisStatsResponse> GetStatsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ManualAnalysisSummaryResponse>> GetRecentAsync(int limit, CancellationToken cancellationToken);
}

public sealed class ManualAnalysisService : IManualAnalysisService
{
    private readonly IInsightSynthesisService _synthesis;
    private readonly TimeProvider _clock;
    private readonly DocMindOptions _options;
    private readonly ILogger<ManualAnalysisService> _logger;

    public ManualAnalysisService(
        IInsightSynthesisService synthesis,
        TimeProvider clock,
        IOptions<DocMindOptions> options,
        ILogger<ManualAnalysisService> logger)
    {
        _synthesis = synthesis;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ManualAnalysisRunResponse?> RunSessionAsync(string sessionId, ManualAnalysisRunRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ValidationException("Session id is required.");
        }

        if (!_options.Manual.EnableSessions)
        {
            throw new InvalidOperationException("Manual analysis sessions are disabled via configuration.");
        }

        var session = await ManualAnalysisSession.Get(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        session.Prompt ??= new ManualAnalysisPrompt();
        session.Prompt.Variables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        session.Documents ??= new List<ManualAnalysisDocument>();
        session.RunHistory ??= new List<ManualAnalysisRun>();

        var documentIds = ResolveDocumentIds(session, request);
        var documents = await LoadDocumentsAsync(documentIds, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0)
        {
            throw new ValidationException("Manual analysis sessions require at least one document.");
        }

        if (!string.IsNullOrWhiteSpace(request?.Instructions))
        {
            session.Prompt.Instructions = request!.Instructions!.Trim();
        }

        if (request?.Variables is { Count: > 0 })
        {
            foreach (var (key, value) in request.Variables)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                session.Prompt.Variables[key] = value ?? string.Empty;
            }
        }

        UpdateSessionDocuments(_clock, session, documents);

        session.Status = ManualAnalysisStatus.Running;
        session.LastRunAt = _clock.GetUtcNow();
        session.UpdatedAt = session.LastRunAt.Value;
        await session.Save(cancellationToken).ConfigureAwait(false);

        SemanticTypeProfile? profile = null;
        if (session.ProfileId.HasValue)
        {
            profile = await SemanticTypeProfile.Get(session.ProfileId.Value.ToString(), cancellationToken).ConfigureAwait(false);
        }

        var result = await _synthesis.GenerateManualSessionAsync(session, profile, documents, cancellationToken).ConfigureAwait(false);

        session.LastSynthesis = result.Synthesis;
        session.Status = ManualAnalysisStatus.Completed;
        session.LastRunAt = _clock.GetUtcNow();
        session.UpdatedAt = session.LastRunAt.Value;
        session.CompletedAt ??= session.LastRunAt;
        session.RunHistory.Add(result.RunTelemetry);
        await session.Save(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Manual analysis session {SessionId} executed with {DocumentCount} documents", session.Id, documents.Count);

        return new ManualAnalysisRunResponse
        {
            Session = session,
            Synthesis = session.LastSynthesis,
            Run = result.RunTelemetry
        };
    }

    public async Task<ManualAnalysisStatsResponse> GetStatsAsync(CancellationToken cancellationToken)
    {
        var sessions = await ManualAnalysisSession.All(cancellationToken).ConfigureAwait(false);
        var list = sessions.ToList();
        var now = _clock.GetUtcNow();

        return new ManualAnalysisStatsResponse
        {
            GeneratedAt = now,
            TotalSessions = list.Count,
            CompletedSessions = list.Count(s => s.Status == ManualAnalysisStatus.Completed),
            RunningSessions = list.Count(s => s.Status == ManualAnalysisStatus.Running),
            DraftSessions = list.Count(s => s.Status is ManualAnalysisStatus.Draft or ManualAnalysisStatus.Ready)
        };
    }

    public async Task<IReadOnlyList<ManualAnalysisSummaryResponse>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        var sessions = await ManualAnalysisSession.All(cancellationToken).ConfigureAwait(false);
        var normalizedLimit = Math.Clamp(limit <= 0 ? 5 : limit, 1, 50);

        var summaries = sessions
            .OrderByDescending(s => s.LastRunAt ?? s.UpdatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .Take(normalizedLimit)
            .Select(session => new ManualAnalysisSummaryResponse
            {
                Id = session.Id,
                Title = session.Title,
                Status = session.Status,
                DocumentCount = session.Documents.Count,
                CreatedAt = session.CreatedAt,
                LastRunAt = session.LastRunAt,
                ProfileId = session.ProfileId?.ToString(),
                PrimaryFinding = session.LastSynthesis?.Findings.FirstOrDefault()?.Body ?? session.LastSynthesis?.FilledTemplate,
                Confidence = session.LastSynthesis?.Confidence
            })
            .ToList();

        return summaries;
    }

    private static IReadOnlyList<Guid> ResolveDocumentIds(ManualAnalysisSession session, ManualAnalysisRunRequest? request)
    {
        var included = session.Documents
            .Where(d => d.IncludeInSynthesis)
            .Select(d => d.SourceDocumentId)
            .ToHashSet();

        if (request?.DocumentIds is { Count: > 0 })
        {
            var overrides = request.DocumentIds
                .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
                .Where(guid => guid != Guid.Empty)
                .ToHashSet();

            if (overrides.Count > 0)
            {
                included.IntersectWith(overrides);
            }
        }

        return included.ToList();
    }

    private static async Task<List<SourceDocument>> LoadDocumentsAsync(IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken)
    {
        var documents = new List<SourceDocument>();
        foreach (var documentId in documentIds.Distinct())
        {
            if (documentId == Guid.Empty)
            {
                continue;
            }

            var document = await SourceDocument.Get(documentId.ToString(), cancellationToken).ConfigureAwait(false);
            if (document is not null)
            {
                documents.Add(document);
            }
        }

        return documents;
    }

    private static void UpdateSessionDocuments(TimeProvider clock, ManualAnalysisSession session, IReadOnlyList<SourceDocument> documents)
    {
        if (session.Documents is null)
        {
            session.Documents = new List<ManualAnalysisDocument>();
        }

        var lookup = session.Documents.ToDictionary(d => d.SourceDocumentId, d => d);
        foreach (var document in documents)
        {
            if (!Guid.TryParse(document.Id, out var documentId))
            {
                continue;
            }

            if (!lookup.TryGetValue(documentId, out var sessionDocument))
            {
                sessionDocument = new ManualAnalysisDocument
                {
                    SourceDocumentId = documentId,
                    IncludeInSynthesis = true,
                    AddedAt = clock.GetUtcNow()
                };
                session.Documents.Add(sessionDocument);
                lookup[documentId] = sessionDocument;
            }

            sessionDocument.DisplayName = document.DisplayName ?? document.FileName;
        }

        if (session.Documents.Count > 0 && session.Status == ManualAnalysisStatus.Draft)
        {
            session.Status = ManualAnalysisStatus.Ready;
        }
    }
}
