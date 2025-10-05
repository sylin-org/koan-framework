using Koan.Data.Core.Model;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Canon.Model;

public sealed class Record : Entity<Record>
{
    public string RecordId { get => Id; set => Id = value; }
    [Index]
    public string SourceId { get; set; } = default!;
    [Index]
    public DateTimeOffset OccurredAt { get; set; }
    public string? PolicyVersion { get; set; }
    [Index]
    public string? CorrelationId { get; set; }
    public object? Data { get; set; }
    public object? Source { get; set; }
    public object? Diagnostics { get; set; }
}

