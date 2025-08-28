using System;

namespace Sora.Orchestration.Attributes;

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