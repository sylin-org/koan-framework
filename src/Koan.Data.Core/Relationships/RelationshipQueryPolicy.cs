namespace Koan.Data.Core.Relationships;

/// <summary>
/// Caller-owned safety bounds for relationship expansion. Strict mode accepts native backend and
/// in-memory-store execution, but never opts a scan-backed provider into materialization.
/// </summary>
public sealed record RelationshipQueryPolicy
{
    public static RelationshipQueryPolicy Strict { get; } = new();

    public int? MaxResults { get; init; }
    public int? MaxFallbackCandidates { get; init; }

    public static RelationshipQueryPolicy Bounded(int maxCandidates, int? maxResults = null)
        => new()
        {
            MaxFallbackCandidates = Positive(maxCandidates, nameof(maxCandidates)),
            MaxResults = OptionalPositive(maxResults, nameof(maxResults))
        };

    internal void Validate()
    {
        _ = OptionalPositive(MaxResults, nameof(MaxResults));
        _ = OptionalPositive(MaxFallbackCandidates, nameof(MaxFallbackCandidates));
    }

    private static int Positive(int value, string name)
        => value is > 0 and < int.MaxValue
            ? value
            : throw new ArgumentOutOfRangeException(name, "A relationship bound must be positive and smaller than Int32.MaxValue.");

    private static int? OptionalPositive(int? value, string name)
        => value is null ? null : Positive(value.Value, name);
}
