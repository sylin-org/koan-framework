using System.ComponentModel;

namespace Koan.AI.Contracts.Models;

public record AiRouteHints
{
    private string? _source;

    /// <summary>
    /// Logical source name, or a pinned member in <c>source::member</c> form.
    /// </summary>
    public string? Source
    {
        get => _source;
        init => _source = value;
    }

    /// <summary>
    /// Compatibility alias for <see cref="Source"/>. Applications select a source; the router
    /// resolves its provider adapter.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? AdapterId
    {
        get => _source;
        init => _source = value;
    }

    public string? Policy { get; init; }
    public string? StickyKey { get; init; }
}
