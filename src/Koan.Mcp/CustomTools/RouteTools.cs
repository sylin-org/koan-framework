using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing.Patterns;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// Projects <c>[Tool]</c> actions as custom tools (ARCH-0141). Name, description and input schema all come from the action,
/// which has no other definition: its route, its parameters and the properties of its input type. Where the application reads by
/// address (AI-0042), a route with values also takes an <c>address</c> in their place. An operation of a declared entity controller takes
/// what it declares (WEB-0087): its route values, the members it writes, its input, a collection's <c>after</c> and <c>size</c> (DATA-0170) and, where its
/// controller serves partitions, <c>set</c>.
/// </summary>
internal static class RouteTools
{
    /// <summary>The tools of every [Tool] route; <paramref name="pagination"/> is the host's page bounds, which a declared collection's
    /// <c>size</c> states as REST clamps it.</summary>
    public static IEnumerable<McpCustomTool> Discover(IActionDescriptorCollectionProvider actions, PaginationSafetyBounds pagination, bool addressed = false)
    {
        var exposed = actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(action => ToolAttribute.Exposes(action.MethodInfo, action.ControllerTypeInfo))
            .ToList();
        // [HttpGet] and [HttpHead] on one action make two descriptors of one read, and that is one tool.
        return exposed.Where(action => !(Answers(action, HttpMethods.Head) && exposed.Any(other => other.MethodInfo == action.MethodInfo
                && other.AttributeRouteInfo?.Template == action.AttributeRouteInfo?.Template && Answers(other, HttpMethods.Get))))
            .Select(action => Build(action, pagination, addressed));
    }

    private static bool Answers(ControllerActionDescriptor action, string method)
        => action.ActionConstraints?.OfType<HttpMethodActionConstraint>().FirstOrDefault()?.HttpMethods.FirstOrDefault() is { } first
           && string.Equals(first, method, StringComparison.OrdinalIgnoreCase);

    private static McpCustomTool Build(ControllerActionDescriptor action, PaginationSafetyBounds pagination, bool addressed)
    {
        var where = $"{action.ControllerTypeInfo.Name}.{action.MethodInfo.Name}";
        if (action.AttributeRouteInfo?.Template is not { } template)
            throw new InvalidOperationException($"[Tool] on {where} needs an attribute route: a tool is a request to one.");

        var pattern = RoutePatternFactory.Parse(template);
        var method = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().FirstOrDefault()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Post;
        var attribute = action.MethodInfo.GetCustomAttribute<ToolAttribute>(inherit: false);
        var name = attribute?.Name ?? Derive(pattern, attribute?.Particle ?? method);
        if (!ToolName.IsGood(name))
            throw new InvalidOperationException($"The tool name '{name}' of {where} is not {ToolName.Rule}. State one with [Tool(Name = ...)].");

        var (schema, query, bodyField, takesAddress) = Input(action, pattern, method, where, pagination, addressed);
        var isGet = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        var description = action.MethodInfo.GetCustomAttribute<DescriptionAttribute>()?.Description;
        // What a route's method promises, and nothing it does not. Whether a POST only adds, or reaches beyond the application, is the action's to say: the specification reads a hint left out as "it may", and a false one here would tell every client that follows it something untrue.
        var stated = attribute?.Stated ?? default;
        var forAll = action.ControllerTypeInfo.GetCustomAttribute<ToolAttribute>(inherit: false)?.Stated ?? default;
        return McpCustomToolRegistry.Assemble(name, description ?? $"{method} /{template}", schema, action.MethodInfo, !isGet,
            readOnly: isGet,
            destructive: isGet ? false : stated.Destructive ?? forAll.Destructive ?? (HttpMethods.IsDelete(method) ? true : null),
            idempotent: isGet || (stated.Idempotent ?? forAll.Idempotent ?? (HttpMethods.IsPut(method) || HttpMethods.IsDelete(method))),
            openWorld: stated.OpenWorld ?? forAll.OpenWorld ?? (isGet ? false : null),
            gateAction: EntityAuthorization.ActionOf(method), route: new RouteTool(action, method, pattern, query, bodyField, takesAddress));
    }

    /// <summary>The route's literal segments, then the particle or HTTP method: <c>/data/posts/{id}</c> with <c>getById</c> makes
    /// <c>data_posts_get_by_id</c>.</summary>
    private static string Derive(RoutePattern pattern, string particle)
        => ToolName.Of(pattern.PathSegments.SelectMany(segment => segment.Parts).OfType<RoutePatternLiteralPart>().Select(part => part.Content).Append(particle));

    private static (JObject Schema, IReadOnlySet<string> Query, string? BodyField, bool TakesAddress) Input(ControllerActionDescriptor action, RoutePattern pattern, string method, string where, PaginationSafetyBounds pagination, bool addressed)
    {
        var properties = new JObject();
        var required = new JArray();
        var query = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? bodyField = null;
        var routed = pattern.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        var bodiless = isRead || HttpMethods.IsDelete(method);
        bool AsQuery(BindingSource? source) => source == BindingSource.Query || (bodiless && source != BindingSource.Body);
        var operation = EntityOperation.IsEntityOperation(action.MethodInfo) ? EntityOperation.Of(action.MethodInfo, action.ControllerTypeInfo) : null;
        var declared = operation?.Declared == true;
        var tookBody = false;

        void Field(string name, Type type, ICustomAttributeProvider[] sources, bool isRequired, bool asQuery)
        {
            var schema = McpCustomToolRegistry.BuildParameterSchema(type);
            var described = sources.SelectMany(source => source.GetCustomAttributes(typeof(DescriptionAttribute), true)).OfType<DescriptionAttribute>().FirstOrDefault();
            if (described is not null) schema["description"] = described.Description;
            if (asQuery && IsComplex(type))
                throw new InvalidOperationException($"{where} reads '{name}', a nested object, from its query, which a tool call cannot carry. Flatten it, or take it from the body.");
            properties[name] = schema;
            if (isRequired) required.Add(name);
            if (asQuery) query.Add(name);
        }

        foreach (var parameter in action.Parameters.OfType<ControllerParameterDescriptor>())
        {
            var source = parameter.BindingInfo?.BindingSource;
            if (parameter.ParameterType == typeof(CancellationToken) || source == BindingSource.Services || source == BindingSource.Special) continue;
            if (source == BindingSource.FormFile || source == BindingSource.Header || typeof(IFormFile).IsAssignableFrom(parameter.ParameterType))
                throw new InvalidOperationException($"{where} takes '{parameter.Name}' from {(source ?? BindingSource.FormFile).DisplayName}, which a tool call cannot carry. Move it to a controller without [Tool].");

            var attributes = new ICustomAttributeProvider[] { parameter.ParameterInfo };
            var bound = parameter.BindingInfo?.BinderModelName ?? parameter.Name;
            // A declared operation takes no value its route and body do not name: a singleton's key is its controller's to know.
            if (declared && !routed.ContainsKey(bound) && source != BindingSource.Body) continue;
            if (routed.TryGetValue(bound, out var route))
                Field(route.Name, parameter.ParameterType, attributes, IsRequired(route), asQuery: false);
            else if (!IsComplex(parameter.ParameterType))
            {
                // A body that is one value, or a list: the argument of that name is the whole body.
                if (source == BindingSource.Body) bodyField = Camel(bound);
                Field(Camel(bound), parameter.ParameterType, attributes, parameter.ParameterInfo.IsDefined(typeof(RequiredAttribute), true), AsQuery(source));
            }
            else
            {
                tookBody |= source == BindingSource.Body;
                var constructor = parameter.ParameterType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                foreach (var property in parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite)
                             .Where(p => source != BindingSource.Body || operation?.Writes is not { } writes || writes.Any(member => member.Name == p.Name)))
                {
                    var argument = constructor?.GetParameters().FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                    ICustomAttributeProvider[] sources = argument is null ? [property] : [property, argument];
                    var named = sources.SelectMany(s => s.GetCustomAttributes(true)).OfType<IModelNameProvider>().FirstOrDefault(n => n.Name is { Length: > 0 })?.Name
                                ?? property.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>()?.PropertyName ?? Camel(property.Name);
                    Field(named, property.PropertyType, sources, sources.Any(s => s.IsDefined(typeof(RequiredAttribute), true)), AsQuery(source));
                }
            }
        }

        if (declared)
        {
            // What a declared operation writes travels in its body, its input beside it or in a read's query, and its partition in the query.
            if (!tookBody && operation!.Writes is { } writes)
                foreach (var member in writes)
                    Field(EntityOperation.WireName(member), member.PropertyType, [member], member.IsDefined(typeof(RequiredAttribute), true), asQuery: false);
            var constructor = operation!.Input?.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            foreach (var member in operation.InputMembers)
            {
                var argument = constructor?.GetParameters().FirstOrDefault(p => string.Equals(p.Name, member.Name, StringComparison.OrdinalIgnoreCase));
                ICustomAttributeProvider[] sources = argument is null ? [member] : [member, argument];
                Field(EntityOperation.WireName(member), member.PropertyType, sources, sources.Any(s => s.IsDefined(typeof(RequiredAttribute), true)), asQuery: bodiless);
            }
            // DATA-0170: a declared collection continues from the `next` of the page before, a page of at most its bound.
            if (operation.Keyset is not null)
            {
                Field(KoanWebConstants.Query.After, typeof(string), [], false, asQuery: true);
                properties[KoanWebConstants.Query.After]!["description"] = "Where the list continues: the 'next' of the page before. Left out, it starts at the beginning.";
                Field(KoanWebConstants.Query.Size, typeof(int), [], false, asQuery: true);
                properties[KoanWebConstants.Query.Size]!["description"] = $"The most rows the page holds, at most {PaginationPolicy.Of(operation.Pagination, pagination).MaxSize}.";
            }
            if (operation.Partitions is { } partitions && !properties.ContainsKey(KoanWebConstants.Query.Set))
            {
                Field(KoanWebConstants.Query.Set, typeof(string), [], false, asQuery: true);
                properties[KoanWebConstants.Query.Set]!["description"] = partitions.Any
                    ? "The partition to act in; left out, the entity's own rows."
                    : $"The partition to act in, one of {string.Join(", ", partitions.Names)}; left out, the entity's own rows.";
            }
        }

        foreach (var parameter in pattern.Parameters.Where(parameter => !properties.ContainsKey(parameter.Name)))
            Field(parameter.Name, typeof(string), [], IsRequired(parameter), asQuery: false);

        // An address names the route's values at once, so where one is offered none of them is required on its own.
        var takesAddress = addressed && pattern.Parameters.Count > 0 && !properties.ContainsKey(AddressReader.Field);
        if (takesAddress)
        {
            var named = string.Join(", ", pattern.Parameters.Select(parameter => parameter.Name));
            properties[AddressReader.Field] = new JObject { ["type"] = "string", ["description"] = $"{AddressReader.AddressHolds} It names what this acts on, in place of {named}." };
            foreach (var parameter in pattern.Parameters)
                if (required.FirstOrDefault(name => string.Equals(name.Value<string>(), parameter.Name, StringComparison.OrdinalIgnoreCase)) is { } listed)
                    required.Remove(listed);
        }

        // A route offers no rehearsal, because its effects are the action's and all it could ever say is that it cannot. One asked for anyway is still answered without running the action.
        var result = new JObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0) result["required"] = required;
        return (result, query, bodyField, takesAddress);
    }

    private static bool IsRequired(RoutePatternParameterPart parameter) => !parameter.IsOptional && parameter.Default is null;

    /// <summary>What MVC binds property by property rather than from one value.</summary>
    private static bool IsComplex(Type type)
        => !TypeDescriptor.GetConverter(Nullable.GetUnderlyingType(type) ?? type).CanConvertFrom(typeof(string))
           && (typeof(IDictionary).IsAssignableFrom(type) || !typeof(IEnumerable).IsAssignableFrom(type));

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
