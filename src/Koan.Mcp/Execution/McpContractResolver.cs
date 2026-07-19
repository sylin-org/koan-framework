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
/// It extends <see cref="CamelCasePropertyNamesContractResolver"/> so ordinary PascalCase CLR models have the
/// same idiomatic camelCase application shape across entity, custom-tool, and Code Mode paths. Protocol DTOs use
/// their protocol-owned serializer and are not governed by this resolver.
/// </summary>
internal sealed class McpContractResolver : CamelCasePropertyNamesContractResolver
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
