using System;
using Koan.Core;
using Koan.Core.Provenance;
using Koan.Mcp.Explorer.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Mcp.Explorer.Initialization;

/// <summary>
/// WEB-0072 — Reference = Intent: referencing <c>Koan.Mcp.Explorer</c> mounts the console. Binds the options
/// and registers the endpoint contributor (WEB-0069), which self-gates on <see cref="McpExplorerOptions.Enabled"/>.
/// </summary>
public sealed class KoanMcpExplorerAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Mcp.Explorer";

    public string? ModuleVersion => typeof(KoanMcpExplorerAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<McpExplorerOptions>().BindConfiguration("Koan:Mcp:Explorer");
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Web.Hosting.IKoanEndpointContributor, McpExplorerEndpointContributor>());
        // AI-0037 D-C — the core owns GET {baseRoute}; the Explorer plugs in its console via this seam (no longer
        // mapping the bare route itself, so there is exactly one owner of the route — no collision with Streamable).
        services.TryAddSingleton<Koan.Mcp.Hosting.IMcpConsoleRenderer, McpConsoleRenderer>();
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);
        module.Describe(ModuleVersion);
    }
}
