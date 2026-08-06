using Koan.Data.Core.Model;

namespace Koan.Data.Cutover.CrossProvider.Tests;

public sealed class CrossProviderRecord : Entity<CrossProviderRecord>
{
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; set; }
    public Guid CorrelationId { get; set; }
    public byte[] Evidence { get; set; } = [];
    public decimal Amount { get; set; }
    public IReadOnlyDictionary<string, string?> Attributes { get; set; } = new Dictionary<string, string?>();
}
