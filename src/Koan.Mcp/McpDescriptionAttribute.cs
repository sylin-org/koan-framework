using System;
using Koan.Web.Endpoints;

namespace Koan.Mcp;

/// <summary>
/// Provides MCP-specific description metadata used when generating JSON schemas.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class McpDescriptionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpDescriptionAttribute"/> class.
    /// </summary>
    /// <param name="description">Description surfaced to MCP clients.</param>
    public McpDescriptionAttribute(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description cannot be null or whitespace.", nameof(description));
        }

        Description = description;
    }

    /// <summary>
    /// Gets the description to surface in MCP schemas.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Limits the description to the specified operation when provided. Defaults to None (applies to all operations).
    /// </summary>
    public EntityEndpointOperationKind Operation { get; set; } = EntityEndpointOperationKind.None;
}
