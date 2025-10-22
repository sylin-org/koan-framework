using System;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core;
using Koan.Mcp.CodeMode.Execution;
using Koan.Mcp.CodeMode.Sdk;
using Koan.Mcp.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Initialization;

/// <summary>
/// Auto-registrar for Koan.Mcp – contributes boot diagnostics for code mode and ensures required services are initialized early.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Mcp";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Services already wired via AddKoanMcp extension; nothing additional required here yet.
        // We could force eager generation of TypeScript SDK by resolving provider in a hosted service if desired.
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // We cannot reliably resolve scoped services here without a provided IServiceProvider. Keep note minimal.
        module.AddNote("CodeMode: diagnostics_unavailable reason=NoServiceProvider");
    }
}

