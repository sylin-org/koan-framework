using System;

namespace Sora.Flow.Options;

public sealed class FlowOptions
{
    public int StandardizeConcurrency { get; set; } = 4;
    public int KeyConcurrency { get; set; } = 4;
    public int AssociateConcurrency { get; set; } = 4;
    public int ProjectConcurrency { get; set; } = 4;

    public int BatchSize { get; set; } = 500;

    // TTLs
    public TimeSpan IntakeTtl { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan StandardizedTtl { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan KeyedTtl { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan ProjectionTaskTtl { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan RejectionReportTtl { get; set; } = TimeSpan.FromDays(30);

    // Background purge
    public bool PurgeEnabled { get; set; } = true;
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(6);

    public bool DeadLetterEnabled { get; set; } = true;

    public bool HooksEnabled { get; set; } = true;

    public string DefaultViewName { get; set; } = Infrastructure.Constants.Views.Materialized;

    // Domain-agnostic aggregation tags (keys) used to derive ReferenceId from normalized dictionaries
    // Example: ["person:employee:id", "person:employee:contact:email", "person:lastSsn"]
    public string[] AggregationTags { get; set; } = Array.Empty<string>();

    // Canonical projection filter: tags that start with any of these prefixes will be excluded
    // from Canonical/Lineage projections. Useful to keep high-churn VO tags (e.g., "reading.") out
    // of domain canonicals.
    public string[] CanonicalExcludeTagPrefixes { get; set; } = Array.Empty<string>();

    // vNext multi-tenancy switch (unused in v1 but reserved)
    public bool MultiTenancyEnabled { get; set; } = false;
    public string? TenantFieldName { get; set; }
}
