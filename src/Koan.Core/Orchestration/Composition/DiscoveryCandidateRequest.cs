using System.Collections.Immutable;
using System.ComponentModel;

namespace Koan.Core.Orchestration.Composition;

/// <summary>
/// Describes one live candidate query for a selected service-discovery adapter.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record DiscoveryCandidateRequest
{
    internal DiscoveryCandidateRequest(
        string serviceName,
        IEnumerable<string> serviceSelectors,
        DiscoveryContext context,
        string? intent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(serviceSelectors);
        ArgumentNullException.ThrowIfNull(context);

        ServiceName = serviceName.Trim();
        ServiceSelectors = serviceSelectors
            .Prepend(ServiceName)
            .Where(static selector => !string.IsNullOrWhiteSpace(selector))
            .Select(static selector => selector.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        Context = context;
        Intent = string.IsNullOrWhiteSpace(intent) ? null : intent.Trim();
    }

    /// <summary>The selected adapter's canonical service name.</summary>
    public string ServiceName { get; }

    /// <summary>
    /// Stable service selectors understood by the adapter, with the canonical name first and aliases after it.
    /// </summary>
    public IReadOnlyList<string> ServiceSelectors { get; }

    /// <summary>The caller's explicit source URI, or <see langword="null"/> during automatic discovery.</summary>
    public string? Intent { get; }

    /// <summary>The application-owned discovery context.</summary>
    public DiscoveryContext Context { get; }
}
