using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Canon.Domain.Model;

/// <summary>
/// Persisted audit entry for canonical property changes.
/// </summary>
public sealed class CanonAuditLog : Entity<CanonAuditLog>
{
    [Index]
    public string CanonicalId { get; set; } = string.Empty;

    [Index]
    public string EntityType { get; set; } = string.Empty;

    [Index]
    public string Property { get; set; } = string.Empty;

    public string Policy { get; set; } = string.Empty;

    public string? PreviousValue { get; set; }

    public string? CurrentValue { get; set; }

    public string? Source { get; set; }

    public string? ArrivalToken { get; set; }

    [Index]
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public Dictionary<string, string?> Evidence
    {
        get => _evidence;
        set => _evidence = value is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string?> _evidence = new(StringComparer.OrdinalIgnoreCase);
}
