using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Mcp.Operations.Initialization;

/// <summary>
/// P3.2 — Reference = Intent: referencing <c>Koan.Mcp.Operations</c> ships the operational toolsets (discovered as
/// <c>Toolset</c> subclasses by the MCP custom-tool registry). Each toolset is then opt-in via
/// <c>Koan:Mcp:Operations:{Jobs,Cache}</c> (default OFF). No DI wiring — the toolsets are DI-instantiated on demand;
/// this registrar only contributes the boot-report line.
/// </summary>
public sealed class McpOperationsModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Nothing to wire: the operational toolsets are discovered by reflection and DI-instantiated on demand; the
        // Koan:Mcp:Operations opt-in binds onto McpServerOptions.Operations via Koan.Mcp's existing options binding.
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);

        var enabled = EnabledToolsets(configuration);
        var active = enabled.Count == 0 ? "(none)" : string.Join(",", enabled);
        var line = $"mcp.ops: available jobs,cache · enabled {active} · grants required · destructive confirm";
        module.Describe(Version, line);
    }

    private static List<string> EnabledToolsets(IConfiguration configuration)
    {
        var section = configuration.GetSection("Koan:Mcp:Operations");
        var enabled = new List<string>();
        foreach (var child in section.GetChildren())
        {
            if (bool.TryParse(child.Value, out var on) && on)
            {
                enabled.Add(child.Key.ToLowerInvariant());
            }
        }
        return enabled.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
