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
        McpTransportMode enabledTransports,
        bool? requireAuthentication)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
        Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        EnabledTransports = enabledTransports;
        RequireAuthentication = requireAuthentication;
    }

    public Type EntityType { get; }

    public Type KeyType { get; }

    public McpEntityAttribute Attribute { get; }

    public EntityEndpointDescriptor Descriptor { get; }

    public IReadOnlyList<McpToolDefinition> Tools { get; }

    public string DisplayName { get; }

    public McpTransportMode EnabledTransports { get; }

    public bool EnableStdio => EnabledTransports.HasFlag(McpTransportMode.Stdio);

    public bool EnableHttpSse => EnabledTransports.HasFlag(McpTransportMode.HttpSse);

    public bool? RequireAuthentication { get; }
}
