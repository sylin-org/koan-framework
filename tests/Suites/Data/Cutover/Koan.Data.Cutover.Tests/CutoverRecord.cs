using Koan.Data.Core.Model;

namespace Koan.Data.Cutover.Tests;

public sealed class CutoverRecord : Entity<CutoverRecord>
{
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; set; }
    public Guid CorrelationId { get; set; }
    public byte[] Evidence { get; set; } = [];
}
