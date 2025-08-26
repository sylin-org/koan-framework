using System;

namespace Sora.Orchestration;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DefaultEndpointAttribute : Attribute
{
    public DefaultEndpointAttribute(string scheme, string defaultHost, int containerPort, string protocol = "tcp", params string[] imagePrefixes)
    {
        Scheme = scheme;
        DefaultHost = defaultHost;
        ContainerPort = containerPort;
        Protocol = protocol;
        ImagePrefixes = imagePrefixes ?? Array.Empty<string>();
    }

    public string Scheme { get; }
    public string DefaultHost { get; }
    public int ContainerPort { get; }
    public string Protocol { get; }
    public string[] ImagePrefixes { get; }
    // Optional URI pattern, e.g. "mongodb://{host}:{port}"; adapters can set via named argument
    public string? UriPattern { get; set; }
}
