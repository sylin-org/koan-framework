using System;
using System.Text.Json;

namespace Koan.Samples.Meridian.Contracts;

public sealed class FieldOverrideRequest
{
    public JsonElement Value { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Reviewer { get; set; } = null;
}

public sealed class FieldOverrideResponse
{
    public string FieldPath { get; set; } = string.Empty;
    public string ValueJson { get; set; } = string.Empty;
    public string? Reason { get; set; } = null;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
