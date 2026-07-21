using System.Text.Json.Serialization;

namespace Koan.Core.Diagnostics;

/// <summary>
/// One credential-redacted, provider-neutral runtime decision or failure. The schema deliberately has no
/// arbitrary payload bag, and every bounded text field passes through the shared credential-shaped redactor.
/// </summary>
public sealed class KoanFact
{
    [JsonConstructor]
    internal KoanFact(
        string code,
        KoanFactKind kind,
        KoanFactState state,
        string subject,
        string summary,
        string reasonCode,
        string? correction,
        string source,
        string correlationId,
        DateTimeOffset observedAtUtc)
    {
        Code = Normalize(code, 160);
        Kind = kind;
        State = state;
        Subject = Normalize(subject, 256);
        Summary = Normalize(summary, 512);
        ReasonCode = Normalize(reasonCode, 160);
        Correction = string.IsNullOrWhiteSpace(correction) ? null : Normalize(correction, 512);
        Source = Normalize(source, 256);
        CorrelationId = Normalize(correlationId, 256);
        ObservedAtUtc = observedAtUtc;
        Id = $"{Code}:{Subject}";
    }

    public string Id { get; }
    public string Code { get; }
    public KoanFactKind Kind { get; }
    public KoanFactState State { get; }
    public string Subject { get; }
    public string Summary { get; }
    public string ReasonCode { get; }
    public string? Correction { get; }
    public string Source { get; }
    public string CorrelationId { get; }
    public DateTimeOffset ObservedAtUtc { get; }

    internal static KoanFact Create(
        string code,
        KoanFactKind kind,
        KoanFactState state,
        string subject,
        string summary,
        string reasonCode,
        string? correction,
        string source,
        string correlationId,
        DateTimeOffset? observedAtUtc = null)
        => new(code, kind, state, subject, summary, reasonCode, correction, source, correlationId,
            observedAtUtc ?? DateTimeOffset.UtcNow);

    private static string Normalize(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        normalized = Redaction.DeIdentify(normalized)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
