namespace Sora.Orchestration.Attributes;

/// <summary>
/// Declares default endpoint for a given mode (Container or Local).
/// </summary>
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