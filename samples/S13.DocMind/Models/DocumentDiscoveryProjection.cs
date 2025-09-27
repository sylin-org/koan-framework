using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Core.Model;
using S13.DocMind.Services;

namespace S13.DocMind.Models;

public sealed class DocumentDiscoveryProjection : Entity<DocumentDiscoveryProjection>
{
    [Required]
    public string Scope { get; set; } = "global";

    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;

    public double? RefreshDurationSeconds { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public DocumentInsightsOverview Overview { get; set; } = new();

    [Column(TypeName = "jsonb")]
    public List<DocumentCollectionSummary> Collections { get; set; } = new();

    [Column(TypeName = "jsonb")]
    public List<AggregationFeedItem> Feed { get; set; } = new();

    [Column(TypeName = "jsonb")]
    public DocumentQueueProjection Queue { get; set; } = new();
}

public sealed class DocumentQueueProjection
{
    public int Pending { get; set; }
    public int Failed { get; set; }
    public DateTimeOffset? OldestQueuedAt { get; set; }
    public DateTimeOffset AsOf { get; set; } = DateTimeOffset.UtcNow;
    public bool HasMore { get; set; }
        = false;
    public int PageSize { get; set; }
        = 0;
    public IReadOnlyCollection<DocumentQueueEntry> Entries { get; set; } = Array.Empty<DocumentQueueEntry>();
}

public sealed class DocumentQueueEntry
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DocumentProcessingStage Stage { get; set; }
    public DocumentProcessingStatus Status { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}
