using System.ComponentModel;

namespace Koan.Data.Core.Routing;

/// <summary>Read-only cross-pillar projection of a repository's mandatory physical route namespace.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IDataRouteBoundRepository
{
    string RouteNamespace { get; }
}
