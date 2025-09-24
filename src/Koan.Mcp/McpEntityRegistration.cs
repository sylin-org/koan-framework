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
        bool enableStdio)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
        Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        EnableStdio = enableStdio;
    }

    public Type EntityType { get; }

    public Type KeyType { get; }

    public McpEntityAttribute Attribute { get; }

    public EntityEndpointDescriptor Descriptor { get; }

    public IReadOnlyList<McpToolDefinition> Tools { get; }

    public string DisplayName { get; }

    public bool EnableStdio { get; }
}
