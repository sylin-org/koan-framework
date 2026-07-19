using System;

namespace Koan.Orchestration.Attributes;

/// <summary>
/// Declares that a service is containerizable and provides default image/ports.
/// Extended properties are provided via named arguments.
/// </summary>
// ARCH-0077 (D2 item 2): RETAINED, not dead surface. No connector applies this today, but the type is still
// read by the manifest generator, the CLI ProjectDependencyAnalyzer, and ComposeExporter. It is retired with
// the orchestration→Aspire migration (ARCH-0077), not piecemeal. See docs/assessment/prompts/PROGRESS.md (D2).
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