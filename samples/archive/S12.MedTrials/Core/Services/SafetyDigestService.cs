using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.Logging;
using S12.MedTrials.Contracts;
using S12.MedTrials.Models;

namespace S12.MedTrials.Services;

public sealed class SafetyDigestService : ISafetyDigestService
{
    private readonly ILogger<SafetyDigestService>? _logger;

    public SafetyDigestService(ILogger<SafetyDigestService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<SafetySummaryResult> SummariseAsync(SafetySummaryRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var lookback = request.LookbackDays <= 0 ? 14 : request.LookbackDays;
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-lookback));

        var allEvents = await AdverseEventReport.All(ct);

        var query = allEvents.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.TrialSiteId))
        {
            query = query.Where(e => e.TrialSiteId == request.TrialSiteId);
        }

        query = query.Where(e => e.OnsetDate >= since);

        if (request.MinimumSeverity.HasValue)
        {
            var min = request.MinimumSeverity.Value;
            query = query.Where(e => e.Severity >= min);
        }

        var events = query.OrderByDescending(e => e.OnsetDate).ToList();

        var max = request.MaxEvents <= 0 ? 25 : Math.Min(request.MaxEvents, 100);
        var eventList = events.Take(max).ToList();
        if (eventList.Count == 0)
        {
            return new SafetySummaryResult($"No adverse events recorded in the last {lookback} days.", false, null, Array.Empty<string>(), eventList);
        }

        var warnings = new List<string>();
        var ai = Ai.TryResolve();
        var degraded = false;
        var model = string.Empty;
        string summary;

        if (ai is not null)
        {
            try
            {
                var prompt = BuildSafetyPrompt(eventList, request, lookback);
                var response = await ai.PromptAsync(new AiChatRequest
                {
                    Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model,
                    Messages =
                    {
                        new AiMessage("system", "You are a clinical safety assistant. Summarise events with bullet points and call out required follow-ups."),
                        new AiMessage("user", prompt)
                    },
                    Options = new AiPromptOptions { MaxOutputTokens = 256 }
                }, ct);

                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    summary = response.Text.Trim();
                    model = response.Model ?? request.Model ?? string.Empty;
                }
                else
                {
                    degraded = true;
                    summary = BuildFallbackSummary(eventList, lookback);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                degraded = true;
                warnings.Add("AI summarisation failed; using deterministic summary.");
                _logger?.LogWarning(ex, "Safety digest AI summarisation failed");
                summary = BuildFallbackSummary(eventList, lookback);
            }
        }
        else
        {
            degraded = true;
            warnings.Add("AI provider unavailable; using deterministic summary.");
            summary = BuildFallbackSummary(eventList, lookback);
        }

        return new SafetySummaryResult(summary, degraded, string.IsNullOrWhiteSpace(model) ? null : model, warnings, eventList);
    }

    private static string BuildSafetyPrompt(IEnumerable<AdverseEventReport> events, SafetySummaryRequest request, int lookbackDays)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Summarise {events.Count()} adverse events from the last {lookbackDays} days.");
        if (!string.IsNullOrWhiteSpace(request.TrialSiteId))
        {
            builder.AppendLine($"Focus on site: {request.TrialSiteId}");
        }

        builder.AppendLine("Events:");
        foreach (var item in events.Take(25))
        {
            builder.AppendLine($"- {item.OnsetDate:yyyy-MM-dd} | Participant {item.ParticipantId} | Severity {item.Severity} | Status {item.Status} | {item.Description}");
        }

        builder.AppendLine("Highlight mandatory follow-ups, escalation status, and trends.");
        return builder.ToString();
    }

    private static string BuildFallbackSummary(IReadOnlyCollection<AdverseEventReport> events, int lookbackDays)
    {
        var total = events.Count;
        var severityBreakdown = events
            .GroupBy(e => e.Severity)
            .OrderByDescending(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key}")
            .ToArray();
        var escalated = events.Count(e => e.Status is AdverseEventStatus.Escalated or AdverseEventStatus.Investigating);
        return $"Reviewed {total} adverse events in the last {lookbackDays} days ({string.Join(", ", severityBreakdown)}); {escalated} require active follow-up.";
    }
}
