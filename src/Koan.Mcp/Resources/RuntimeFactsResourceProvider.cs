using System.Security.Claims;
using Koan.Core.Diagnostics;
using Koan.Web.Infrastructure;
using Koan.Web.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Resources;

/// <summary>
/// Agent-readable projection of the same redacted facts used by startup and health. SEC-0018: listed and readable
/// only where <c>/.well-known/Koan/facts</c> is (<see cref="ObservabilityExposure"/>), or for a <c>null</c> principal
/// (privileged in-process tooling).
/// </summary>
public sealed class RuntimeFactsResourceProvider : IMcpResourceProvider
{
    public const string ResourceUri = "koan://facts";

    private readonly IKoanRuntimeFacts _runtimeFacts;
    private readonly bool _exposed;

    public RuntimeFactsResourceProvider(IKoanRuntimeFacts runtimeFacts, IHostEnvironment environment, IOptions<KoanWebOptions> webOptions)
    {
        _runtimeFacts = runtimeFacts ?? throw new ArgumentNullException(nameof(runtimeFacts));
        _exposed = ObservabilityExposure.Allowed(environment, webOptions?.Value);
    }

    /// <summary>Without the host environment the exposure decision cannot be made, so the facts stay unexposed.</summary>
    [Obsolete("Cannot tell whether the host exposes its facts, so it serves them to null-principal tooling only. Pass the host environment and KoanWebOptions (SEC-0018).")]
    public RuntimeFactsResourceProvider(IKoanRuntimeFacts runtimeFacts)
    {
        _runtimeFacts = runtimeFacts ?? throw new ArgumentNullException(nameof(runtimeFacts));
        _exposed = false;
    }

    private bool Visible(ClaimsPrincipal? user) => user is null || _exposed;

    public IEnumerable<McpResourceDescriptor> List(ClaimsPrincipal? user)
    {
        if (!Visible(user)) yield break;
        yield return new McpResourceDescriptor(
            ResourceUri,
            "Koan runtime facts",
            "Versioned composition decisions, degraded states, and corrective guidance for this host.",
            "application/json");
    }

    public McpResourceContents? Read(string uri, ClaimsPrincipal? user)
        => string.Equals(uri, ResourceUri, StringComparison.OrdinalIgnoreCase) && Visible(user)
            ? new McpResourceContents(
                ResourceUri,
                "application/json",
                KoanFactJson.Serialize(_runtimeFacts.Current))
            : null;
}
