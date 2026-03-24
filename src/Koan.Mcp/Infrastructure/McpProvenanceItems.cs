using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;
using Koan.Mcp.Options;

namespace Koan.Mcp.Infrastructure;

internal static class McpProvenanceItems
{
    private static readonly McpServerOptions Defaults = new();
    private static readonly CodeModeOptions DefaultCodeMode = Defaults.CodeMode ?? new();

    private static readonly IReadOnlyCollection<string> TransportConsumers = new[] { "Koan.Mcp.Server" };
    private static readonly IReadOnlyCollection<string> ExposureConsumers = new[] { "Koan.Mcp.Capabilities" };
    private static readonly IReadOnlyCollection<string> RoutingConsumers = new[] { "Koan.Mcp.Routing" };
    private static readonly IReadOnlyCollection<string> EntitiesConsumers = new[] { "Koan.Mcp.EntityRegistry" };
    private static readonly IReadOnlyCollection<string> CodeModeConsumers = new[] { "Koan.Mcp.CodeMode" };
    private static readonly IReadOnlyCollection<string> SandboxConsumers = new[] { "Koan.Mcp.CodeMode.Sandbox" };

    internal static readonly ProvenanceItem EnableStdioTransport = new(
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.EnableStdioTransport),
        "Enable STDIO Transport",
        "Hosts the STDIO MCP transport within the current process.",
        DefaultValue: BoolString(Defaults.EnableStdioTransport),
        DefaultConsumers: TransportConsumers);

    internal static readonly ProvenanceItem EnableHttpSseTransport = new(
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.EnableHttpSseTransport),
        "Enable HTTP + SSE Transport",
        "Hosts the HTTP + Server-Sent Events MCP transport.",
        DefaultValue: BoolString(Defaults.EnableHttpSseTransport),
        DefaultConsumers: TransportConsumers);

    internal static readonly ProvenanceItem RequireAuthentication = new(
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.RequireAuthentication),
        "Require Authentication",
        "Gates HTTP + SSE transport endpoints behind authentication.",
        DefaultValue: BoolString(Defaults.RequireAuthentication),
        DefaultConsumers: TransportConsumers);

    internal static readonly ProvenanceItem HttpSseRoute = new(
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.HttpSseRoute),
        "HTTP + SSE Route",
        "Base route for MCP HTTP + SSE endpoints.",
        DefaultValue: Defaults.HttpSseRoute,
        DefaultConsumers: RoutingConsumers);

    internal static readonly ProvenanceItem PublishCapabilityEndpoint = new(
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.PublishCapabilityEndpoint),
        "Publish Capability Endpoint",
        "Exposes the MCP capability discovery endpoint.",
        DefaultValue: BoolString(Defaults.PublishCapabilityEndpoint),
        DefaultConsumers: ExposureConsumers);

    internal static readonly ProvenanceItem AllowedEntities = new(
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.AllowedEntities),
        "Allowed Entities",
        "Explicit allow-list for MCP entities exposed to clients.",
        DefaultValue: FormatList(Defaults.AllowedEntities),
        DefaultConsumers: EntitiesConsumers);

    internal static readonly ProvenanceItem DeniedEntities = new(
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.DeniedEntities),
        "Denied Entities",
        "Entities filtered from exposure even if registered.",
        DefaultValue: FormatList(Defaults.DeniedEntities),
        DefaultConsumers: EntitiesConsumers);

    internal static readonly ProvenanceItem ExposureMode = new(
        ConfigurationConstants.FullKey(ConfigurationConstants.Keys.Exposure),
        "Exposure Mode",
        "Controls which MCP capabilities are exposed (Auto, Code, Tools, Full).",
        DefaultValue: Defaults.Exposure?.ToString() ?? "Auto",
        DefaultConsumers: ExposureConsumers);

    internal static readonly ProvenanceItem CodeModeEnabled = new(
        $"{ConfigurationConstants.CodeMode.Section}:{ConfigurationConstants.CodeMode.Keys.Enabled}",
        "Code Mode Enabled",
        "Enables MCP code execution features.",
        DefaultValue: BoolString(DefaultCodeMode.Enabled),
        DefaultConsumers: CodeModeConsumers);

    internal static readonly ProvenanceItem CodeModeRuntime = new(
        $"{ConfigurationConstants.CodeMode.Section}:{ConfigurationConstants.CodeMode.Keys.Runtime}",
        "Code Mode Runtime",
        "JavaScript runtime used for code mode execution.",
        DefaultValue: DefaultCodeMode.Runtime,
        DefaultConsumers: CodeModeConsumers);

    internal static readonly ProvenanceItem SandboxCpuMs = new(
        $"{ConfigurationConstants.CodeMode.Sandbox.Section}:{ConfigurationConstants.CodeMode.Sandbox.Keys.CpuMilliseconds}",
        "Sandbox CPU Budget (ms)",
        "CPU time budget allocated per code execution.",
        DefaultValue: DefaultCodeMode.Sandbox.CpuMilliseconds.ToString(),
        DefaultConsumers: SandboxConsumers);

    internal static readonly ProvenanceItem SandboxMemoryMb = new(
        $"{ConfigurationConstants.CodeMode.Sandbox.Section}:{ConfigurationConstants.CodeMode.Sandbox.Keys.MemoryMegabytes}",
        "Sandbox Memory Budget (MB)",
        "Memory budget allocated per code execution.",
        DefaultValue: DefaultCodeMode.Sandbox.MemoryMegabytes.ToString(),
        DefaultConsumers: SandboxConsumers);

    internal static readonly ProvenanceItem SandboxMaxRecursion = new(
        $"{ConfigurationConstants.CodeMode.Sandbox.Section}:{ConfigurationConstants.CodeMode.Sandbox.Keys.MaxRecursionDepth}",
        "Sandbox Max Recursion",
        "Maximum recursion depth allowed within code execution.",
        DefaultValue: DefaultCodeMode.Sandbox.MaxRecursionDepth.ToString(),
        DefaultConsumers: SandboxConsumers);

    private static string BoolString(bool value) => value ? "true" : "false";

    private static string FormatList(IEnumerable<string> items)
    {
        if (items is null)
        {
            return "(none)";
        }

        var buffer = new List<string>();
        foreach (var item in items)
        {
            var value = (item ?? "").Trim();
            if (value.Length > 0)
            {
                buffer.Add(value);
            }
        }

        return buffer.Count == 0 ? "(none)" : string.Join(", ", buffer);
    }
}
