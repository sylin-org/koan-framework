using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Sora.Flow.Model;

// Generic control command for Flow (plain POCO; not a FlowValueObject).
public sealed class ControlCommand
{
    public string Verb { get; set; } = string.Empty;
    // Target can be "system:adapter" or "system:*"
    public string? Target { get; set; }
    public string? Arg { get; set; }
    // Optional bag for verb-specific parameters (JSON-friendly payload)
    public Dictionary<string, JsonElement> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
}
