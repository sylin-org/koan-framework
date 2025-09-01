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

/// <summary>
/// Marks a value-object property as a link to a Flow entity, enabling generic mapping and indexing.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class EntityLinkAttribute : Attribute
{
    public Type FlowEntityType { get; }
    public LinkKind Kind { get; }

    public EntityLinkAttribute(Type flowEntityType, LinkKind kind = LinkKind.CanonicalId)
    {
        FlowEntityType = flowEntityType ?? throw new ArgumentNullException(nameof(flowEntityType));
        Kind = kind;
    }
}

/// <summary>
/// Declares whether an entity link property carries a canonical ULID or an external ID to be resolved.
/// </summary>
public enum LinkKind
{
    CanonicalId = 0,
    ExternalId = 1,
}

/// <summary>
/// Declares that a type is a Flow value-object and specifies its parent Flow entity type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class FlowValueObjectAttribute : Attribute
{
    public Type Parent { get; }
    public FlowValueObjectAttribute(Type parent)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
    }
}

/// <summary>
/// Marks the property that contains the parent entity's canonical business key for association.
/// Optionally provide a payload path used in StagePayload when the intake dictionary uses a different key.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ParentKeyAttribute : Attribute
{
    /// <summary>
    /// Optional key name/path in the intake payload dictionary used to extract the parent key.
    /// If null, the property name is used as the fallback.
    /// </summary>
    public string? PayloadPath { get; }
    public ParentKeyAttribute(string? payloadPath = null)
    { PayloadPath = string.IsNullOrWhiteSpace(payloadPath) ? null : payloadPath; }
}
