using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Koan.Web.OpenApi.Schema;

/// <summary>
/// X-openapi-newtonsoft-fidelity — makes the System.Text.Json metadata that <c>Microsoft.AspNetCore.OpenApi</c>
/// reads MIRROR Koan's Newtonsoft REST wire, so the generated OpenAPI document describes the ACTUAL contract, not
/// the CLR shape. Koan serializes REST via Newtonsoft (<c>CamelCasePropertyNamesContractResolver</c> +
/// <c>StringEnumConverter</c> + <c>[JsonProperty]</c> renames); the STJ-reflective OpenAPI generator is blind to
/// that, so the doc misnames renamed fields and emits opaque integer enums.
///
/// Rather than patch the generated document per-schema, we fix the SOURCE on the System.Text.Json options the
/// generator reads (<c>Microsoft.AspNetCore.Http.Json.JsonOptions</c>). The Newtonsoft formatter still owns the REST
/// wire; these options drive OpenAPI schema generation AND minimal-API JSON responses — so this also makes those
/// responses honor the same contract (string enums) as the rest of the API:
/// <list type="number">
///   <item><b>String enums</b> — <see cref="JsonStringEnumConverter"/> matches <c>StringEnumConverter</c>. API-first:
///   an enum is a semantic string member, never an opaque integer. Applied globally (the wire is already string;
///   this aligns the doc and any STJ-serialized response).</item>
///   <item><b>Property names</b> — for members carrying an explicit Newtonsoft <c>[JsonProperty(Name)]</c>, the
///   STJ name is set to the SAME resolved wire name Newtonsoft produces (faithful by construction — we re-read
///   Newtonsoft's own output, never re-implement its naming rules). Conservative on purpose: unrenamed members are
///   left untouched (the Http default is already camelCase), so a non-Koan type's own naming is never clobbered.</item>
///   <item><b>Hidden fields</b> — a member with Newtonsoft <c>[JsonIgnore]</c> is off the wire (and, because
///   Newtonsoft is also Koan's persistence serializer, off storage), so the doc omits it too. (Distinct from
///   <c>[McpIgnore]</c>, which hides from the agent only — REST keeps the field, so the doc keeps it.)</item>
/// </list>
/// </summary>
public static class NewtonsoftSchemaMirror
{
    // The same resolver Koan.Web's AddNewtonsoftJson configures — the single source of truth for wire names.
    private static readonly IContractResolver Newtonsoft = new CamelCasePropertyNamesContractResolver();

    /// <summary>Configure a STJ options object to mirror the Newtonsoft wire for OpenAPI schema generation.</summary>
    public static void Apply(JsonSerializerOptions json)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));

        if (!json.Converters.Any(c => c is JsonStringEnumConverter))
        {
            json.Converters.Add(new JsonStringEnumConverter());
        }

        json.TypeInfoResolver = (json.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(MirrorNewtonsoftNames);
    }

    private static void MirrorNewtonsoftNames(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;
        if (Newtonsoft.ResolveContract(typeInfo.Type) is not JsonObjectContract contract) return;

        // CLR member name → the Newtonsoft RESOLVED wire name (camelCase + [JsonProperty] override, whatever NS does).
        var wireNames = contract.Properties
            .Where(p => p is { UnderlyingName: not null, PropertyName: not null, Ignored: false })
            .GroupBy(p => p.UnderlyingName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().PropertyName!, StringComparer.Ordinal);

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].AttributeProvider is not MemberInfo member) continue;

            // A Newtonsoft [JsonIgnore] keeps the field off the REST wire — and, because Newtonsoft is also Koan's
            // persistence serializer, off storage — so the doc must omit it too. STJ doesn't read Newtonsoft's
            // [JsonIgnore], so mirror that intent here. (This is the honest way to hide a field from the contract;
            // [McpIgnore] is deliberately NOT this — it hides from the agent only, REST keeps the field.)
            if (member.GetCustomAttribute<Newtonsoft.Json.JsonIgnoreAttribute>() is not null)
            {
                typeInfo.Properties.RemoveAt(i);
                continue;
            }

            // Mirror an EXPLICIT [JsonProperty(Name)] rename to the same resolved wire name Newtonsoft emits. The
            // Http default is already camelCase (matching the resolver), so unrenamed members need no change — and
            // staying conservative means we never override a non-Koan type's own [JsonPropertyName] when these
            // options also serialize minimal-API responses.
            if (member.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName is { Length: > 0 }
                && wireNames.TryGetValue(member.Name, out var wireName))
            {
                typeInfo.Properties[i].Name = wireName;
            }
        }
    }
}
