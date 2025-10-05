using System;
using System.Linq;

namespace Koan.Canon.Attributes;

/// <summary>
/// Declares a dotted-path aggregation tag on a Canonical model property.
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
/// Overrides the model name used for Canon set naming (Canon.{model}.**).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CanonModelAttribute : Attribute
{
    public string Name { get; }
    public CanonModelAttribute(string name) => Name = name;
}

/// <summary>
/// Opt-out marker for discovery if a type should not be considered a Canon model.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CanonIgnoreAttribute : Attribute { }

// EntityLink, CanonValueObject, and ParentKey attributes removed in favor of unified ParentAttribute from Koan.Data.Core.
// Use [Parent(typeof(ParentType))] from Koan.Data.Core.Relationships namespace instead.

/// <summary>
/// Marks a class as a Canon orchestrator, enabling auto-registration and message processing
/// for Canon entity transport envelopes from the dedicated "Koan.Canon.CanonEntity" queue.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CanonOrchestratorAttribute : Attribute { }

/// <summary>
/// Marks a property as an aggregation key for strongly-typed CanonEntity models.
/// The property value will be used for entity identification and aggregation across sources.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AggregationKeyAttribute : Attribute { }

/// <summary>
/// Declares aggregation keys at the class level for DynamicCanonEntity models.
/// These keys are JSON paths used to identify and aggregate entities from multiple sources.
/// Example: [AggregationKeys("identifier.username", "identifier.employeeId")]
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AggregationKeysAttribute : Attribute
{
    public string[] Keys { get; }
    
    public AggregationKeysAttribute(params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            throw new ArgumentException("At least one aggregation key must be specified", nameof(keys));
        
        Keys = keys.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();
        
        if (Keys.Length == 0)
            throw new ArgumentException("At least one non-empty aggregation key must be specified", nameof(keys));
    }
}



