using System;

namespace Sora.Orchestration;

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
}
