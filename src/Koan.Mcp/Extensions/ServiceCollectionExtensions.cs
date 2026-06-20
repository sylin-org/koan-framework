using System;
using Koan.Mcp.CodeMode.Execution;
using Koan.Mcp.CodeMode.Json; // Added for AddCodeModeJson extension
using Koan.Mcp.CodeExecution;
using Koan.Mcp.CodeMode.Sdk;
using Koan.Mcp.Diagnostics;
using Koan.Mcp.Execution;
using Koan.Mcp.Hosting;
using Koan.Mcp.Infrastructure;
using Koan.Mcp.Options;
using Koan.Mcp.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Mcp.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanMcp(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        if (configuration is not null)
        {
            services.AddOptions<McpServerOptions>().Bind(configuration.GetSection(ConfigurationConstants.Section));
        }
        else
        {
            services.AddOptions<McpServerOptions>().BindConfiguration(ConfigurationConstants.Section);
        }

        services.TryAddSingleton<SchemaBuilder>();
        services.TryAddSingleton<DescriptorMapper>();
        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
        services.TryAddSingleton<McpEntityRegistry>();
        services.TryAddSingleton<Koan.Mcp.CustomTools.McpCustomToolRegistry>();
        services.TryAddSingleton<Koan.Mcp.CustomTools.McpCustomToolInvoker>();
        services.TryAddSingleton<RequestTranslator>();
        services.TryAddSingleton<ResponseTranslator>();
        services.TryAddSingleton<EndpointToolExecutor>();
        // WEB-0072: the per-caller surface projection (backs the Explorer console + {baseRoute}/map.json).
        services.TryAddSingleton<Koan.Mcp.Resources.McpSurfaceProjector>();
        services.TryAddSingleton<IMcpTransportDispatcher, StreamJsonRpcTransportDispatcher>();
        services.TryAddSingleton<McpServer>();
        services.AddHostedService<StdioTransport>();

        // P1.2: introspection resources. The framework ships koan://entities; apps (and AN8's koan://self)
        // add more via TryAddEnumerable(ServiceDescriptor.Singleton<IMcpResourceProvider, …>()).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Mcp.Resources.IMcpResourceProvider, Koan.Mcp.Resources.EntityCatalogResourceProvider>());
        // AN8: koan://self self-introduction (prose + structured), per grant.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Mcp.Resources.IMcpResourceProvider, Koan.Mcp.Resources.SelfResourceProvider>());

        services.TryAddSingleton<HttpSseSessionManager>();
        services.AddHostedService(sp => sp.GetRequiredService<HttpSseSessionManager>());
        services.TryAddSingleton<HttpSseTransport>();
        services.TryAddSingleton<IMcpCapabilityReporter, HttpSseCapabilityReporter>();

        services.AddCors();

    // Code mode services
    // JSON facade (Newtonsoft-backed) for code-mode dynamic operations
    services.AddCodeModeJson();
        services.AddOptions<CodeModeOptions>().BindConfiguration(ConfigurationConstants.CodeMode.Section);
        services.AddOptions<SandboxOptions>().BindConfiguration(ConfigurationConstants.CodeMode.Sandbox.Section);
        services.AddOptions<TypeScriptSdkOptions>().BindConfiguration(ConfigurationConstants.CodeMode.TypeScript.Section);
        services.TryAddSingleton<Koan.Mcp.CodeExecution.ICodeExecutor, JintCodeExecutor>();
        services.TryAddSingleton<TypeScriptSdkGenerator>();
        services.TryAddSingleton<TypeScriptSdkProvider>();
        services.TryAddSingleton<ITypeScriptSdkProvider>(sp => sp.GetRequiredService<TypeScriptSdkProvider>());
    services.TryAddScoped<KoanSdkBindings>();
        // SEC-0004 Phase 3.3: per-scope holder for the code-mode caller principal (set at the execution-scope
        // boundary by JintCodeExecutor, read by the SDK entity proxy).
        services.TryAddScoped<McpCallContext>();

        return services;
    }
}
