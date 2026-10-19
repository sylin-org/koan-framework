using Koan.Mcp.TestKit;
using Koan.Web.Hooks;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots Koan + MCP with the <see cref="TicketTitleSearch"/> hook registered for <see cref="Ticket"/>.</summary>
public sealed class QueryTextFixture : McpHarnessFixtureBase
{
    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IRequestOptionsHook<Ticket>, TicketTitleSearch>();
}
