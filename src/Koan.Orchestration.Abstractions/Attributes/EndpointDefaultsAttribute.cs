namespace Koan.Orchestration.Attributes;

/// <summary>
/// Declares default endpoint for a given mode (Container or Local).
/// </summary>
// ARCH-0077 (D2 item 2): RETAINED, not dead surface. No connector applies this today, but the type is still
// read by the manifest generator, the CLI ProjectDependencyAnalyzer, and ComposeExporter. It is retired with
// the orchestration→Aspire migration (ARCH-0077), not piecemeal. See docs/assessment/prompts/PROGRESS.md (D2).
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EndpointDefaultsAttribute : Attribute
{
    public EndpointDefaultsAttribute(EndpointMode mode, string scheme, string host, int port)
    {
        Mode = mode;
        Scheme = scheme;
        Host = host;
        Port = port;
    }

    public EndpointMode Mode { get; }
    public string Scheme { get; }
    public string Host { get; }
    public int Port { get; }

    /// <summary>Optional URI pattern; tokens: {scheme},{host},{port},{serviceId}.</summary>
    public string? UriPattern { get; set; }
}