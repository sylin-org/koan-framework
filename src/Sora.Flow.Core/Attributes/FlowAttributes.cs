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
