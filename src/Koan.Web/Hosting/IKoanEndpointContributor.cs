using Microsoft.AspNetCore.Routing;

namespace Koan.Web.Hosting;

/// <summary>
/// The supported seam for a module to map endpoints inside Koan's single <c>UseEndpoints</c> block (after
/// <c>MapControllers</c>). Replaces ad-hoc reflection into a module's endpoint-mapping extension. A module that
/// owns endpoints (e.g. MCP) references Koan.Web and ships one of these. See WEB-0069.
/// </summary>
public interface IKoanEndpointContributor
{
    /// <summary>Tie-break; lower maps first.</summary>
    int Order => 0;

    void Map(IEndpointRouteBuilder endpoints);
}
