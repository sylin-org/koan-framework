using System.Security.Claims;
using Koan.Core.Diagnostics;

namespace Koan.Mcp.Resources;

/// <summary>Agent-readable projection of the same redacted facts used by startup and health.</summary>
public sealed class RuntimeFactsResourceProvider(IKoanRuntimeFacts runtimeFacts) : IMcpResourceProvider
{
    public const string ResourceUri = "koan://facts";

    public IEnumerable<McpResourceDescriptor> List(ClaimsPrincipal? user)
    {
        yield return new McpResourceDescriptor(
            ResourceUri,
            "Koan runtime facts",
            "Versioned composition decisions, degraded states, and corrective guidance for this host.",
            "application/json");
    }

    public McpResourceContents? Read(string uri, ClaimsPrincipal? user)
        => string.Equals(uri, ResourceUri, StringComparison.OrdinalIgnoreCase)
            ? new McpResourceContents(
                ResourceUri,
                "application/json",
                KoanFactJson.Serialize(runtimeFacts.Current))
            : null;
}
