using System;

namespace Sora.Orchestration.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SoraAppAttribute : Attribute
{
    public SoraAppAttribute()
    {
    }

    // Optional metadata; generator will read when present
    public int DefaultPublicPort { get; set; }
    public string? AppCode { get; set; }
    public string? AppName { get; set; }
    public string? Description { get; set; }

    // Optional: unified capabilities (map encoded as key[=value] items)
    // Example: new[] { "http", "swagger", "graphql", "auth=oidc" }
    public string[]? Capabilities { get; set; }
}
