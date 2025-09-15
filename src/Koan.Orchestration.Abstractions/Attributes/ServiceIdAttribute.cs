using System;

namespace Koan.Orchestration.Attributes;

/// <summary>
/// Declares a stable logical identifier for a service (e.g., "mongo").
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ServiceIdAttribute : Attribute
{
    public ServiceIdAttribute(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    public string Id { get; }
}
