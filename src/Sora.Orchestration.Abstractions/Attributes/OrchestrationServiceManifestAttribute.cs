using System;
using System.Collections.Generic;

namespace Sora.Orchestration;

/// <summary>
/// Declarative manifest describing the dev-time container shape for an adapter/provider.
/// Applied at the assembly level by adapter packages to enable discovery without hardcoding.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class OrchestrationServiceManifestAttribute : Attribute
{
    /// <param name="id">Stable service id (e.g., "mongo", "weaviate", "ollama").</param>
    /// <param name="image">Container image (e.g., "mongo:7").</param>
    /// <param name="containerPorts">Container ports exposed by the service.</param>
    public OrchestrationServiceManifestAttribute(string id, string image, int[] containerPorts)
    {
        Id = id;
        Image = image;
        ContainerPorts = containerPorts ?? Array.Empty<int>();
    }

    public string Id { get; }
    public string Image { get; }
    public IReadOnlyList<int> ContainerPorts { get; }

    /// <summary>
    /// Environment variables for the service (KEY=VALUE). Optional.
    /// </summary>
    public string[]? Environment { get; set; }

    /// <summary>
    /// Host volume bindings in form "./Data/NAME:/container/path" or named volumes in form "NAME:/container/path".
    /// Optional.
    /// </summary>
    public string[]? Volumes { get; set; }

    /// <summary>
    /// Environment variables to inject into the App service to connect to this service.
    /// Supports tokens {serviceId} and {port} (first container port).
    /// Example: "Sora__Data__Mongo__ConnectionString=mongodb://{serviceId}:{port}".
    /// </summary>
    public string[]? AppEnvironment { get; set; }

    // Optional health probe settings for container-mode readiness (HTTP-based).
    // Compose exporter will generate a curl test to http://{id}:{firstPort}{HealthPath} with the timings below when provided.
    public string? HealthPath { get; set; }
    public int HealthIntervalSeconds { get; set; }
    public int HealthTimeoutSeconds { get; set; }
    public int HealthRetries { get; set; }
}
