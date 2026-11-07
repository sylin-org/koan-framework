using System;
using System.Collections.Generic;
using Koan.Web.Endpoints;

namespace Koan.Mcp;

public sealed class McpEntityRegistration
{
    public McpEntityRegistration(
        Type entityType,
        Type keyType,
        McpEntityAttribute attribute,
        EntityEndpointDescriptor descriptor,
        IReadOnlyList<McpToolDefinition> tools,
        string displayName,
        Type? realization = null)
    {
        Realization = realization;
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
        Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    public Type EntityType { get; }

    public Type KeyType { get; }

    public McpEntityAttribute Attribute { get; }

    public EntityEndpointDescriptor Descriptor { get; }

    public IReadOnlyList<McpToolDefinition> Tools { get; }

    public string DisplayName { get; }

    /// <summary>The class that realizes the entity's tools (an <c>EntityToolset&lt;T&gt;</c>, or an <c>EntityController&lt;T&gt;</c> carrying
    /// <c>[Tool]</c>), or null for the terse <c>[McpEntity]</c>. A controller's partitions bind its tools as they bind its routes (WEB-0087).</summary>
    public Type? Realization { get; }

}
