using System;

namespace Sora.Orchestration.Attributes;

/// <summary>
/// Declares default HTTP health probe settings for a service. Intended for container mode.
/// Provide a relative path (e.g., "/healthz"), and optional timings in seconds.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HealthEndpointDefaultsAttribute : Attribute
{
    /// <param name="httpPath">Relative HTTP path (e.g., "/healthz" or "/v1/.well-known/ready").</param>
    public HealthEndpointDefaultsAttribute(string httpPath)
    {
        HttpPath = httpPath ?? string.Empty;
    }

    /// <summary>Relative HTTP path (e.g., "/healthz").</summary>
    public string HttpPath { get; }

    /// <summary>Interval between checks, in seconds.</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>Timeout for each check, in seconds.</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>Number of retries before marking unhealthy.</summary>
    public int Retries { get; set; }
}
