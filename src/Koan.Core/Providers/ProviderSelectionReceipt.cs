using System.ComponentModel;
using Koan.Core.Semantics;

namespace Koan.Core.Providers;

/// <summary>
/// Safe immutable evidence for a completed typed provider choice. It contains identifiers and reason codes only;
/// pillar-specific configuration, endpoints, credentials, tenant values, and exception text do not belong here.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProviderSelectionReceipt
{
    public ProviderSelectionReceipt(
        string subject,
        string providerId,
        ProviderIntentPosture intent,
        int priority,
        string reason,
        bool directIntent = false,
        IReadOnlyList<string>? rejectedReasonCodes = null)
    {
        Subject = Normalize(subject, nameof(subject));
        ProviderId = Normalize(providerId, nameof(providerId));
        Intent = intent;
        Priority = priority;
        Reason = Normalize(reason, nameof(reason));
        DirectIntent = directIntent;
        RejectedReasonCodes = (rejectedReasonCodes ?? [])
            .Select(code => Normalize(code, nameof(rejectedReasonCodes)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public string Subject { get; }
    public string ProviderId { get; }
    public ProviderIntentPosture Intent { get; }
    public int Priority { get; }
    public string Reason { get; }
    public bool DirectIntent { get; }
    public IReadOnlyList<string> RejectedReasonCodes { get; }

    private static string Normalize(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Selection receipt identifiers cannot be empty.", parameter);
        return new SemanticId(value).Value;
    }
}
