using System.ComponentModel;

namespace Koan.Data.Core.Decorators;

/// <summary>Immutable physical-route identity supplied to route-aware repository decorators.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record DataRepositoryDecorationContext(
    string Source,
    string Adapter,
    string RouteNamespace)
{
    public static DataRepositoryDecorationContext Unbound { get; } = new(string.Empty, string.Empty, string.Empty);
}
