using System;

namespace Sora.Orchestration.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HostMountAttribute : Attribute
{
    public HostMountAttribute(string containerPath)
    {
        if (string.IsNullOrWhiteSpace(containerPath))
            throw new ArgumentException("Container path is required", nameof(containerPath));
        ContainerPath = containerPath;
    }

    // Absolute path inside container, e.g. "/var/lib/postgresql/data"
    public string ContainerPath { get; }
}
