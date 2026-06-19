using Koan.Mcp.TestKit;
using Koan.Web.Extensions;
using Koan.Web.Hooks;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.RelationshipVisibility.Tests;

/// <summary>
/// Boots Koan + MCP + Web hosting <see cref="Maker"/> / <see cref="Work"/> with their visibility hooks,
/// via the reusable <see cref="McpHarnessFixtureBase"/>. AN-leak's MCP coverage proves the endpoint-layer
/// governed expansion reaches the MCP <c>EndpointToolExecutor</c> path (MCP rides the same
/// <c>IEntityEndpointService</c>, so no governance is duplicated in the transport).
/// </summary>
public sealed class RelationshipVisibilityFixture : McpHarnessFixtureBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddKoanControllersFrom<MakersController>();
        services.AddKoanControllersFrom<WorksController>();
        services.AddSingleton<IRequestOptionsHook<Maker>, MakerVisibilityHook>();
        services.AddSingleton<IRequestOptionsHook<Work>, WorkVisibilityHook>();
    }
}
