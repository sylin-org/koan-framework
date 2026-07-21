using System.ComponentModel;

namespace Koan.Core.Providers;

/// <summary>One normalized immutable candidate inside a host-owned typed provider catalog.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProviderCandidate<TProvider> where TProvider : class
{
    internal ProviderCandidate(
        TProvider value,
        string id,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> referenceIdentities,
        int priority)
    {
        Value = value;
        Id = id;
        Aliases = aliases;
        ReferenceIdentities = referenceIdentities;
        Priority = priority;
    }

    public TProvider Value { get; }
    public string Id { get; }
    public IReadOnlyList<string> Aliases { get; }
    public IReadOnlyList<string> ReferenceIdentities { get; }
    public int Priority { get; }
}
