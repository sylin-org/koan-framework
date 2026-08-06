using System.ComponentModel;

namespace Koan.Data.Core.Routing;

/// <summary>Cross-pillar read-only projection of the current Data operation's mandatory route namespace.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DataRouteOperationContext
{
    public static string? CurrentNamespace => DataOperationHorizon.CurrentNamespace;
}
