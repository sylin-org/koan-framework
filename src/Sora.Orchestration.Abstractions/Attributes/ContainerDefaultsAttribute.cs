using System;

namespace Sora.Orchestration.Abstractions.Attributes;

/// <summary>
/// Declares that a service is containerizable and provides default image/ports.
/// Extended properties are provided via named arguments.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ContainerDefaultsAttribute : Attribute
{
    public ContainerDefaultsAttribute(string image)
    {
        if (string.IsNullOrWhiteSpace(image)) throw new ArgumentException("image is required", nameof(image));
        Image = image;
    }

    /// <summary>Image repository/name (without tag).</summary>
    public string Image { get; }

    /// <summary>Optional tag (e.g., "latest", "7").</summary>
    public string? Tag { get; set; }

    /// <summary>Container-exposed ports (container side).</summary>
    public int[]? Ports { get; set; }

    /// <summary>Environment entries as KEY=VALUE pairs.</summary>
    public string[]? Env { get; set; }

    /// <summary>Volumes as HOST_PATH:CONTAINER_PATH pairs.</summary>
    public string[]? Volumes { get; set; }

    /// <summary>Optional healthcheck test command (e.g., CMD-SHELL ...).</summary>
    public string? HealthCheck { get; set; }

    public string? HealthCheckInterval { get; set; }
    public string? HealthCheckTimeout { get; set; }
    public int? HealthCheckRetries { get; set; }
    public string? HealthCheckStartPeriod { get; set; }
}

public enum EndpointMode
{
    Container = 0,
    Local = 1
}

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

/// <summary>
/// Declares default app environment variables. Provide as KEY=VALUE pairs.
/// Values can use tokens: {scheme},{host},{port},{serviceId} resolved against the selected mode.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AppEnvDefaultsAttribute : Attribute
{
    public AppEnvDefaultsAttribute(params string[] pairs)
    {
        Pairs = pairs ?? Array.Empty<string>();
    }

    public string[] Pairs { get; }
}
