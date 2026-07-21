using System;
using Koan.Web.Endpoints;

namespace Koan.Mcp;

/// <summary>
/// ARCH-0092 §H — overrides the template description of a built-in entity verb on an
/// <see cref="EntityToolset{TEntity,TKey}"/>. Attribute-first tuning (no builder DSL); mirrors the
/// per-operation pattern of <c>[McpDescription(Operation = …)]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ToolDescriptionAttribute : Attribute
{
    public ToolDescriptionAttribute(EntityEndpointOperationKind operation, string description)
    {
        Operation = operation;
        Description = description;
    }

    public EntityEndpointOperationKind Operation { get; }
    public string Description { get; }
}

/// <summary>
/// ARCH-0092 §H — absolutely removes a built-in entity verb from an <see cref="EntityToolset{TEntity,TKey}"/>'s
/// MCP surface. Distinct from a per-grant Wall (which hides per caller): a hidden op is never exposed as a
/// tool to anyone.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ToolHiddenAttribute : Attribute
{
    public ToolHiddenAttribute(EntityEndpointOperationKind operation) => Operation = operation;

    public EntityEndpointOperationKind Operation { get; }
}
