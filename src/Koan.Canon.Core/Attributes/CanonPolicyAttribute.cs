using System;

namespace Koan.Canon.Attributes;

/// <summary>
/// Configures external ID correlation policies for Canon entities.
/// Enables automatic external ID generation and cross-system correlation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CanonPolicyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the external ID correlation policy.
    /// </summary>
    public ExternalIdPolicy ExternalIdPolicy { get; set; } = ExternalIdPolicy.AutoPopulate;
    
    /// <summary>
    /// Gets or sets the property name to use for external ID generation.
    /// If not specified, uses the first [Key] property for strong-typed entities
    /// or "id" property for dynamic entities.
    /// </summary>
    public string? ExternalIdKey { get; set; }
}

/// <summary>
/// Defines the external ID correlation policy for Canon entities.
/// </summary>
public enum ExternalIdPolicy
{
    /// <summary>
    /// Automatically generate external IDs from source system metadata during Canonical projection.
    /// Uses identifier.external.{system} pattern with [Key] property value or specified ExternalIdKey.
    /// </summary>
    AutoPopulate,
    
    /// <summary>
    /// Manual external ID specification - developer explicitly provides identifier.external.* keys.
    /// Framework processes provided external IDs but doesn't auto-generate them.
    /// </summary>
    Manual,
    
    /// <summary>
    /// Disable external ID tracking entirely for this entity type.
    /// No external ID processing or IdentityLink creation.
    /// </summary>
    Disabled,
    
    /// <summary>
    /// Track source system only without individual entity IDs.
    /// Creates identifier.external.{system} without specific entity identifier values.
    /// </summary>
    SourceOnly
}

