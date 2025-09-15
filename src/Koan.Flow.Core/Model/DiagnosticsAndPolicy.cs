using Koan.Data.Core.Model;

namespace Koan.Flow.Diagnostics;

public sealed class RejectionReport : Entity<RejectionReport>
{
    public string ReasonCode { get; set; } = default!;
    public string? EvidenceJson { get; set; }
    public string? PolicyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
