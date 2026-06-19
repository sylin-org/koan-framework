using Koan.Mcp.TestKit;
using Koan.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots Koan + MCP + Web hosting <see cref="Gadget"/> and the <c>gadget_ping</c> custom verb
/// for the wire-shape (AN1) and annotation (AN4) conformance specs.</summary>
public sealed class ConformanceFixture : McpHarnessFixtureBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddKoanControllersFrom<GadgetsController>();
    }
}
