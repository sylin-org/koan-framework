using System.ComponentModel;

namespace Koan.Core.Providers;

/// <summary>
/// Framework-infrastructure declaration of one provider's stable identity and deterministic fallback rank.
/// Pillars retain eligibility, precedence, scoring, and failure policy.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProviderCandidateDescriptor
{
    public ProviderCandidateDescriptor(
        string id,
        IReadOnlyList<string>? aliases = null,
        IReadOnlyList<string>? referenceIdentities = null,
        int priority = 0)
    {
        Id = id;
        Aliases = aliases ?? [];
        ReferenceIdentities = referenceIdentities ?? [];
        Priority = priority;
    }

    public string Id { get; }
    public IReadOnlyList<string> Aliases { get; }
    public IReadOnlyList<string> ReferenceIdentities { get; }
    public int Priority { get; }
}
