using System;

namespace Koan.Canon.Attributes;

/// <summary>
/// Decorate adapter hosts to declare system/adapter identity and default behaviors.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CanonAdapterAttribute : Attribute
{
    public string System { get; }
    public string Adapter { get; }
    public string? DefaultSource { get; init; }
    public string[] Policies { get; init; } = Array.Empty<string>();
    public string[] Capabilities { get; init; } = Array.Empty<string>();

    public CanonAdapterAttribute(string system, string adapter)
    {
        System = system;
        Adapter = adapter;
    }
}


