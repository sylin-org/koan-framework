using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;

namespace Sora.Flow.Model;

public sealed class KeyIndex : Entity<KeyIndex>
{
    public string AggregationKey { get => Id; set => Id = value; }
    [Index]
    public string ReferenceId { get; set; } = default!;
}

public sealed class ReferenceItem : Entity<ReferenceItem>
{
    public string ReferenceId { get => Id; set => Id = value; }
    [Index]
    public ulong Version { get; set; }
    public bool RequiresProjection { get; set; }
}
