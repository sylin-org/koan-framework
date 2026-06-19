using Koan.Mcp.TestKit;
using Koan.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots Koan + MCP + Web hosting <see cref="Author"/> + <see cref="Article"/> (with two
/// same-target relationship edges) for the AN7 governed edge-traversal specs. Separate from the other
/// fixtures so the relationship entity set never perturbs the AN1/AN4/P1.2 specs.</summary>
public sealed class EdgeFixture : McpHarnessFixtureBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddKoanControllersFrom<AuthorsController>();
        services.AddKoanControllersFrom<ArticlesController>();
    }
}
