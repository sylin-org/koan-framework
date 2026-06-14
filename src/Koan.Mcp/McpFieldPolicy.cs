using System;
using System.Reflection;
using Newtonsoft.Json;

namespace Koan.Mcp;

/// <summary>
/// Central policy for per-property MCP exposure. Shared by schema generation
/// (<see cref="Koan.Mcp.Schema.SchemaBuilder"/>) and serialization
/// (<see cref="Koan.Mcp.Execution.McpContractResolver"/>) so exclusion and wire-name resolution
/// stay consistent across the schema, results, and input paths.
/// </summary>
internal static class McpFieldPolicy
{
    /// <summary>True when the member must not be serialized into MCP results.</summary>
    public static bool IsExcludedFromOutput(MemberInfo member)
    {
        var attribute = member.GetCustomAttribute<McpIgnoreAttribute>(inherit: true);
        return attribute is not null && (attribute.Direction & McpFieldDirection.Output) != 0;
    }

    /// <summary>True when the member must not appear in input schemas or be set from caller payloads.</summary>
    public static bool IsExcludedFromInput(MemberInfo member)
    {
        var attribute = member.GetCustomAttribute<McpIgnoreAttribute>(inherit: true);
        return attribute is not null && (attribute.Direction & McpFieldDirection.Input) != 0;
    }

    /// <summary>
    /// Resolves the JSON wire name for a member, honoring a Newtonsoft <c>[JsonProperty("name")]</c> rename.
    /// The MCP result/input serializers use the default (PascalCase) contract resolver, so the wire name is
    /// the rename when present, otherwise the CLR member name. Keeping the schema in sync with this prevents
    /// the input schema from advertising a property name the entity never round-trips.
    /// </summary>
    public static string ResolveWireName(MemberInfo member)
    {
        var jsonProperty = member.GetCustomAttribute<JsonPropertyAttribute>(inherit: true);
        if (jsonProperty is not null && !string.IsNullOrWhiteSpace(jsonProperty.PropertyName))
        {
            return jsonProperty.PropertyName!;
        }

        return member.Name;
    }
}
