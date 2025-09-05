using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;

namespace Sora.Flow.Model;

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
    public object? StagePayload { get; set; }
    public object? Diagnostics { get; set; }
}
