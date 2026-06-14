using System;

namespace Koan.Mcp;

/// <summary>
/// Directions a property can be excluded from when exposed via MCP.
/// </summary>
[Flags]
public enum McpFieldDirection
{
    /// <summary>No exclusion.</summary>
    None = 0,

    /// <summary>
    /// Exclude from caller-supplied input: the property is dropped from upsert/patch input schemas and
    /// can never be set from a tool argument payload (mass-assignment guard).
    /// </summary>
    Input = 1,

    /// <summary>
    /// Exclude from results: the property is never serialized into get/query/collection results
    /// (Tools mode and Code Mode share this path).
    /// </summary>
    Output = 2,

    /// <summary>Exclude from both input and output (the safe default for internal / PII fields).</summary>
    Both = Input | Output
}

/// <summary>
/// Hides a property from MCP exposure. Applied consistently to schema generation, result serialization,
/// and input deserialization so an entity carrying internal or PII fields can be exposed read-only
/// (or read-write) through MCP without leaking those fields.
/// </summary>
/// <remarks>
/// This is MCP-local on purpose. Unlike <c>[Newtonsoft.Json.JsonIgnore]</c>, it does not affect storage
/// (the canonical persistence serializer is also Newtonsoft, so a Newtonsoft <c>[JsonIgnore]</c> would
/// silently drop the field from the data store as well); unlike <c>[System.Text.Json.Serialization.JsonIgnore]</c>,
/// it is actually honored by the Newtonsoft-based MCP serializer.
/// </remarks>
/// <example>
/// <code>
/// [McpEntity(AllowMutations = false)]
/// public class Work : Entity&lt;Work&gt;
/// {
///     public string Title { get; set; } = "";
///
///     [McpIgnore]                              // hidden from input and output
///     public List&lt;string&gt; ClaimedByUserIds { get; set; } = new();
///
///     [McpIgnore(McpFieldDirection.Input)]    // server-owned; returned, but callers cannot set it
///     public string? SourceSightingId { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class McpIgnoreAttribute : Attribute
{
    /// <summary>Initializes a new instance that excludes the member from both input and output.</summary>
    public McpIgnoreAttribute()
    {
    }

    /// <summary>Initializes a new instance that excludes the member in the specified direction(s).</summary>
    public McpIgnoreAttribute(McpFieldDirection direction)
    {
        Direction = direction;
    }

    /// <summary>The direction(s) in which the member is excluded. Defaults to <see cref="McpFieldDirection.Both"/>.</summary>
    public McpFieldDirection Direction { get; set; } = McpFieldDirection.Both;
}
