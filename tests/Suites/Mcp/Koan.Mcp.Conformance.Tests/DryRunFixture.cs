using Koan.Mcp.TestKit;
using Koan.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots Koan + MCP + Web hosting <see cref="Widget"/> and the <c>widget_recompute</c> custom verb
/// for the AN11 dry-run / state-delta / did-you-mean / partial-rehearsal specs. Separate from
/// <see cref="ConformanceFixture"/> so the AN11 entity set never perturbs the AN1/AN4 wire-shape specs.</summary>
public sealed class DryRunFixture : McpHarnessFixtureBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddKoanControllersFrom<WidgetsController>();
    }
}
