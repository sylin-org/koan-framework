using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S12.MedTrials.Models;

namespace S12.MedTrials.Infrastructure;

public sealed class MedTrialsSeedWorker : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MedTrialsSeedWorker>? _logger;

    public MedTrialsSeedWorker(IServiceProvider services, ILogger<MedTrialsSeedWorker>? logger = null)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            if (await TrialSite.Count(cancellationToken).ConfigureAwait(false) > 0)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;

            var sites = new[]
            {
                new TrialSite
                {
                    Name = "Riverside Clinical Center",
                    Location = "Seattle, WA",
                    PrincipalInvestigator = "Dr. Priya Desai",
                    EnrollmentTarget = 120,
                    CurrentEnrollment = 78,
                    RegulatoryStatus = "Active",
                    Phase = "Phase III",
                    UpdatedAt = now
                },
                new TrialSite
                {
                    Name = "Sunrise Research Hospital",
                    Location = "Austin, TX",
                    PrincipalInvestigator = "Dr. Mateo Alvarez",
                    EnrollmentTarget = 90,
                    CurrentEnrollment = 52,
                    RegulatoryStatus = "Active",
                    Phase = "Phase II",
                    UpdatedAt = now
                }
            };
            await TrialSite.UpsertMany(sites, cancellationToken).ConfigureAwait(false);

            var visits = new List<ParticipantVisit>
            {
                new ParticipantVisit
                {
                    TrialSiteId = sites[0].Id,
                    ParticipantId = "P-10045",
                    VisitType = VisitType.Treatment,
                    ScheduledAt = now.AddDays(2).AddHours(9),
                    Status = VisitStatus.Scheduled,
                    Cohort = "Dose A",
                    WindowLabel = "Week 6",
                    UpdatedAt = now
                },
                new ParticipantVisit
                {
                    TrialSiteId = sites[0].Id,
                    ParticipantId = "P-10057",
                    VisitType = VisitType.FollowUp,
                    ScheduledAt = now.AddDays(2).AddHours(11),
                    Status = VisitStatus.Scheduled,
                    Cohort = "Dose A",
                    WindowLabel = "Week 6",
                    UpdatedAt = now
                },
                new ParticipantVisit
                {
                    TrialSiteId = sites[0].Id,
                    ParticipantId = "P-10112",
                    VisitType = VisitType.Treatment,
                    ScheduledAt = now.AddDays(2).AddHours(12),
                    Status = VisitStatus.Scheduled,
                    Cohort = "Dose B",
                    WindowLabel = "Week 3",
                    UpdatedAt = now
                },
                new ParticipantVisit
                {
                    TrialSiteId = sites[1].Id,
                    ParticipantId = "P-20411",
                    VisitType = VisitType.Screening,
                    ScheduledAt = now.AddDays(1).AddHours(10),
                    Status = VisitStatus.Scheduled,
                    Cohort = "Lead-in",
                    WindowLabel = "Week 0",
                    UpdatedAt = now
                },
                new ParticipantVisit
                {
                    TrialSiteId = sites[1].Id,
                    ParticipantId = "P-20412",
                    VisitType = VisitType.Treatment,
                    ScheduledAt = now.AddDays(5).AddHours(9),
                    Status = VisitStatus.Scheduled,
                    Cohort = "Dose Expansion",
                    WindowLabel = "Week 2",
                    UpdatedAt = now
                }
            };
            await ParticipantVisit.UpsertMany(visits, cancellationToken).ConfigureAwait(false);

            var documents = new[]
            {
                new ProtocolDocument
                {
                    TrialSiteId = sites[0].Id,
                    Title = "Protocol Amendment 3",
                    DocumentType = "Protocol",
                    Version = "v3.0",
                    ExtractedText = "Updated dosing guidance and safety monitoring windows for combination arm.",
                    Tags = new[] { "dosing", "safety" },
                    EffectiveDate = now.AddDays(-3),
                    IngestedAt = now
                },
                new ProtocolDocument
                {
                    TrialSiteId = sites[1].Id,
                    Title = "Pharmacy Manual Revision",
                    DocumentType = "Manual",
                    Version = "2025-02",
                    ExtractedText = "Refrigeration checks increased to twice daily; report deviations within 2 hours.",
                    Tags = new[] { "pharmacy", "monitoring" },
                    EffectiveDate = now.AddDays(-10),
                    IngestedAt = now.AddDays(-1)
                }
            };
            await ProtocolDocument.UpsertMany(documents, cancellationToken).ConfigureAwait(false);

            var events = new[]
            {
                new AdverseEventReport
                {
                    TrialSiteId = sites[0].Id,
                    ParticipantId = "P-10045",
                    Severity = AdverseEventSeverity.Moderate,
                    Description = "Injection site reaction with mild fever managed with acetaminophen.",
                    OnsetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                    Status = AdverseEventStatus.Investigating,
                    ReportedAt = now.AddDays(-2),
                    UpdatedAt = now
                },
                new AdverseEventReport
                {
                    TrialSiteId = sites[1].Id,
                    ParticipantId = "P-20411",
                    Severity = AdverseEventSeverity.Severe,
                    Description = "Transient hypotension post-dose requiring observation and hydration.",
                    OnsetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    Status = AdverseEventStatus.Escalated,
                    ReportedAt = now.AddDays(-1),
                    UpdatedAt = now
                }
            };
            await AdverseEventReport.UpsertMany(events, cancellationToken).ConfigureAwait(false);

            var notes = new[]
            {
                new MonitoringNote
                {
                    TrialSiteId = sites[0].Id,
                    NoteType = "CRA Visit",
                    Summary = "Temperature logs updated; follow-up on vaccine storage audit scheduled for next site call.",
                    FollowUpRequired = true,
                    EnteredBy = "Allison Chen",
                    CreatedAt = now.AddDays(-1),
                    Tags = new[] { "storage", "qa" }
                }
            };
            await MonitoringNote.UpsertMany(notes, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Seeded S12.MedTrials sample data.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to seed S12.MedTrials sample data");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
