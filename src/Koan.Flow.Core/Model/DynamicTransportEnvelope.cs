using System;
using System.Collections.Generic;

namespace Koan.Flow.Model;

/// <summary>
/// Transport envelope for dynamic Flow entities. Similar to TransportEnvelope but uses
/// a dictionary payload instead of strongly typed payload. The generic type parameter
/// informs the receptor which Flow intake queue to route the dynamic object to.
/// </summary>
/// <typeparam name="T">The target Flow entity type (used for routing only)</typeparam>
public class DynamicTransportEnvelope<T>
{
    /// <summary>Transport envelope version</summary>
    public string Version { get; set; } = "1";

    /// <summary>Source identifier</summary>
    public string? Source { get; set; }

    /// <summary>Model name</summary>
    public string Model { get; set; } = typeof(T).Name;

    /// <summary>Envelope type identifier</summary>
    public string Type { get; set; } = $"DynamicTransportEnvelope<{typeof(T).FullName}>";

    /// <summary>Dynamic payload as dictionary with JSON paths</summary>
    public Dictionary<string, object?> Payload { get; set; } = new();

    /// <summary>Transport timestamp</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Transport metadata</summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();
}