using System.Reflection;
using Koan.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Koan.Mcp.Execution;

/// <summary>
/// Newtonsoft contract resolver that honors <see cref="McpIgnoreAttribute"/>. The same instance is shared by
/// the result serializer (<see cref="ResponseTranslator"/>) and the input deserializer
/// (<see cref="RequestTranslator"/>):
/// <list type="bullet">
///   <item>output-excluded members are not readable, so they never appear in tool results (Tools or Code Mode);</item>
///   <item>input-excluded members are not writable, so caller payloads cannot set them (mass-assignment guard).</item>
/// </list>
/// It extends <see cref="DefaultContractResolver"/> (PascalCase, no naming strategy) to preserve the existing
/// MCP wire shape — adding it changes nothing other than the exclusion behavior.
/// </summary>
internal sealed class McpContractResolver : DefaultContractResolver
{
    public static readonly McpContractResolver Instance = new();

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);

        if (McpFieldPolicy.IsExcludedFromOutput(member))
        {
            property.Readable = false;
            property.ShouldSerialize = _ => false;
        }

        if (McpFieldPolicy.IsExcludedFromInput(member))
        {
            property.Writable = false;
        }

        return property;
    }
}
