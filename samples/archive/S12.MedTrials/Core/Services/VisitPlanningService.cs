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

public sealed class VisitPlanningService : IVisitPlanningService
{
    private readonly ILogger<VisitPlanningService>? _logger;

    public VisitPlanningService(ILogger<VisitPlanningService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<VisitPlanningResult> PlanAsync(VisitPlanningRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.TrialSiteId))
        {
            throw new ArgumentException("TrialSiteId is required", nameof(request));
        }

        var participantFilter = request.ParticipantIds is { Length: > 0 }
            ? new HashSet<string>(request.ParticipantIds, StringComparer.OrdinalIgnoreCase)
            : null;

        var allVisits = await ParticipantVisit.All(ct);

        var query = allVisits.AsEnumerable().Where(v => v.TrialSiteId == request.TrialSiteId);

        if (participantFilter is not null)
        {
            query = query.Where(v => participantFilter.Contains(v.ParticipantId));
        }

        if (request.StartWindow.HasValue)
        {
            var start = request.StartWindow.Value;
            query = query.Where(v => v.ScheduledAt >= start);
        }

        if (request.EndWindow.HasValue)
        {
            var end = request.EndWindow.Value;
            query = query.Where(v => v.ScheduledAt <= end);
        }

        var visitList = query.ToList();
        if (visitList.Count == 0)
        {
            return new VisitPlanningResult(Array.Empty<VisitAdjustment>(), true, "No visits found for the supplied criteria.", null, Array.Empty<VisitDiagnostic>());
        }

        var now = DateTimeOffset.UtcNow;
        var maxPerDay = Math.Clamp(request.MaxVisitsPerDay <= 0 ? 12 : request.MaxVisitsPerDay, 3, 24);
        var capacityMap = visitList
            .Where(v => v.Status is VisitStatus.Scheduled or VisitStatus.Proposed)
            .GroupBy(v => DateOnly.FromDateTime(v.ScheduledAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Count());

        var adjustments = new List<VisitAdjustment>();
        var diagnostics = new List<VisitDiagnostic>();

        foreach (var visit in visitList)
        {
            visit.ProposedAdjustments ??= new List<VisitAdjustment>();
            visit.Diagnostics ??= new List<VisitDiagnostic>();
            visit.ProposedAdjustments.RemoveAll(a => string.Equals(a.Source, "planner-heuristic", StringComparison.OrdinalIgnoreCase));
            visit.Diagnostics.RemoveAll(d => string.Equals(d.Code, "capacity-overflow", StringComparison.OrdinalIgnoreCase));
        }

        var overflowGroups = visitList
            .Where(v => v.Status is VisitStatus.Scheduled or VisitStatus.Proposed)
            .GroupBy(v => DateOnly.FromDateTime(v.ScheduledAt.UtcDateTime))
            .ToList();

        foreach (var group in overflowGroups)
        {
            if (!capacityMap.TryGetValue(group.Key, out var scheduledCount))
            {
                continue;
            }

            if (scheduledCount <= maxPerDay)
            {
                continue;
            }

            var overflow = group
                .OrderBy(v => v.ScheduledAt)
                .Skip(maxPerDay)
                .ToList();

            foreach (var visit in overflow)
            {
                var nextSlot = NextAvailableSlot(visit.ScheduledAt, request.AllowWeekendVisits, capacityMap, maxPerDay);
                var adjustment = new VisitAdjustment
                {
                    VisitId = visit.Id,
                    ProposedBy = "planner-heuristic",
                    ProposedAt = now,
                    SuggestedDate = nextSlot,
                    Summary = $"Shift visit to {nextSlot:yyyy-MM-dd HH:mm}",
                    Rationale = $"Exceeded capacity of {maxPerDay} visits on {group.Key:yyyy-MM-dd}.",
                    Source = "planner-heuristic",
                    RequiresApproval = true
                };

                var diagnostic = new VisitDiagnostic
                {
                    Code = "capacity-overflow",
                    Message = $"Overflow detected on {group.Key:yyyy-MM-dd}; proposed move to {nextSlot:yyyy-MM-dd HH:mm}.",
                    Severity = DiagnosticSeverity.Warning,
                    RecordedAt = now
                };

                visit.ProposedAdjustments.Add(adjustment);
                visit.Diagnostics.Add(diagnostic);
                visit.UpdatedAt = now;

                adjustments.Add(adjustment);
                diagnostics.Add(diagnostic);
            }
        }

        if (adjustments.Count > 0)
        {
            await ParticipantVisit.UpsertMany(visitList, ct);
        }

        var ai = Ai.TryResolve();
        var narrative = string.Empty;
        var model = string.Empty;
        var degraded = false;

        if (ai is not null)
        {
            try
            {
                var prompt = BuildPlannerPrompt(visitList, adjustments, request, maxPerDay);
                var response = await ai.PromptAsync(new AiChatRequest
                {
                    Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model,
                    Messages =
                    {
                        new AiMessage("system", "You are a clinical operations assistant. Provide a concise narrative (<120 words) explaining the proposed visit adjustments and highlight any risks."),
                        new AiMessage("user", prompt)
                    },
                    Options = new AiPromptOptions { MaxOutputTokens = 256 }
                }, ct);

                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    narrative = response.Text.Trim();
                    model = response.Model ?? request.Model ?? string.Empty;
                }
                else
                {
                    degraded = true;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                degraded = true;
                _logger?.LogWarning(ex, "AI planner narrative failed for site {SiteId}", request.TrialSiteId);
            }
        }
        else
        {
            degraded = true;
        }

        if (string.IsNullOrWhiteSpace(narrative))
        {
            narrative = BuildFallbackNarrative(adjustments, visitList, maxPerDay, request.AllowWeekendVisits);
        }

        return new VisitPlanningResult(
            adjustments,
            degraded,
            narrative,
            string.IsNullOrWhiteSpace(model) ? null : model,
            diagnostics);
    }

    private static DateTimeOffset NextAvailableSlot(DateTimeOffset original, bool allowWeekends, IDictionary<DateOnly, int> capacityMap, int maxPerDay)
    {
        var current = DateOnly.FromDateTime(original.UtcDateTime);
        var time = TimeOnly.FromTimeSpan(original.TimeOfDay);
        var offset = original.Offset;

        for (var i = 1; i <= 30; i++)
        {
            current = current.AddDays(1);
            if (!allowWeekends && (current.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                continue;
            }

            var count = capacityMap.TryGetValue(current, out var existing) ? existing : 0;
            if (count >= maxPerDay)
            {
                continue;
            }

            capacityMap[current] = count + 1;
            var dateTime = current.ToDateTime(time, DateTimeKind.Unspecified);
            return new DateTimeOffset(dateTime, offset);
        }

        return original.AddDays(1);
    }

    private static string BuildPlannerPrompt(IEnumerable<ParticipantVisit> visits, IEnumerable<VisitAdjustment> adjustments, VisitPlanningRequest request, int maxPerDay)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Site: {request.TrialSiteId}");
        builder.AppendLine($"Max visits per day: {maxPerDay}");
        if (request.StartWindow.HasValue || request.EndWindow.HasValue)
        {
            builder.AppendLine($"Window: {request.StartWindow:yyyy-MM-dd} - {request.EndWindow:yyyy-MM-dd}");
        }

        builder.AppendLine("Current schedule:");
        foreach (var visit in visits.OrderBy(v => v.ScheduledAt))
        {
            builder.AppendLine($"- {visit.ParticipantId} {visit.VisitType} {visit.ScheduledAt:yyyy-MM-dd HH:mm} ({visit.Status})");
        }

        if (adjustments.Any())
        {
            builder.AppendLine("Proposed adjustments:");
            foreach (var adjustment in adjustments)
            {
                builder.AppendLine($"- {adjustment.Summary} | Reason: {adjustment.Rationale}");
            }
        }
        else
        {
            builder.AppendLine("No heuristic adjustments were required. Provide proactive guidance.");
        }

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            builder.AppendLine($"Coordinator notes: {request.Notes}");
        }

        builder.AppendLine("Respond with a concise narrative summarising the schedule health and any recommended follow-ups.");
        return builder.ToString();
    }

    private static string BuildFallbackNarrative(IReadOnlyCollection<VisitAdjustment> adjustments, IReadOnlyCollection<ParticipantVisit> visits, int maxPerDay, bool allowWeekend)
    {
        var affectedParticipants = adjustments
            .Select(a => visits.FirstOrDefault(v => string.Equals(v.Id, a.VisitId, StringComparison.OrdinalIgnoreCase))?.ParticipantId)
            .Where(pid => !string.IsNullOrWhiteSpace(pid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var weekendPolicy = allowWeekend ? "weekend work permitted" : "weekends protected";
        return adjustments.Count == 0
            ? $"Reviewed {visits.Count} visits with a daily cap of {maxPerDay} ({weekendPolicy}); no schedule changes required."
            : $"Generated {adjustments.Count} proposed shifts impacting {affectedParticipants.Length} participants to honour the {maxPerDay}/day cap with {weekendPolicy}. Review and approve before publishing.";
    }
}
