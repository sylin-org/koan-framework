using System;

namespace Sora.Flow.Attributes;

/// <summary>
/// Declares a dotted-path aggregation tag on a canonical model property.
/// Multiple attributes are allowed; paths are case-insensitive.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class AggregationTagAttribute : Attribute
{
    public string Path { get; }
    public AggregationTagAttribute(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Aggregation tag path cannot be null or empty", nameof(path));
        Path = path;
    }
}

/// <summary>
/// Overrides the model name used for Flow set naming (flow.{model}.**).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class FlowModelAttribute : Attribute
{
    public string Name { get; }
    public FlowModelAttribute(string name) => Name = name;
}

/// <summary>
/// Opt-out marker for discovery if a type should not be considered a Flow model.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class FlowIgnoreAttribute : Attribute { }

// EntityLink and FlowValueObject attributes removed in favor of ParentKey with explicit parent typing.

/// <summary>
/// Marks a property as a link to a parent Flow entity. The value can be a canonical ULID, an aggregation key,
/// or an external identifier value that will be resolved server-side using the message source (identifier.external.{source}).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class ParentKeyAttribute : Attribute
{
    public Type Parent { get; }
    public string? Role { get; }
    /// Optional key name/path in the intake payload dictionary used to extract the parent key. If null, uses property name.
    public string? PayloadPath { get; }

    public ParentKeyAttribute(Type parent, string? role = null, string? payloadPath = null)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        Role = string.IsNullOrWhiteSpace(role) ? null : role;
        PayloadPath = string.IsNullOrWhiteSpace(payloadPath) ? null : payloadPath;
    }
}
