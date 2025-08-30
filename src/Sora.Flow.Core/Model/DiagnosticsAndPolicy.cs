using Sora.Data.Core.Model;

namespace Sora.Flow.Model;

public sealed class RejectionReport : Entity<RejectionReport>
{
    public string ReasonCode { get; set; } = default!;
    public string? EvidenceJson { get; set; }
    public string? PolicyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PolicyBundle : Entity<PolicyBundle>
{
    public string Name { get => Id; set => Id = value; }
    public string Version { get; set; } = "1";
    public object? Content { get; set; }
}
