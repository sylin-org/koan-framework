using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using Koan.Mcp.Execution;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
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
/// which has no other definition: its route, its parameters and the properties of its input type.
/// </summary>
internal static partial class RouteTools
{
    /// <summary>Lower-case words joined by underscores, within what every client takes: MCP allows a dot, and Claude's API refuses
    /// one; its documents state 64 characters on one page and 128 on another.</summary>
    [GeneratedRegex("^[a-z0-9_]{1,64}$")]
    private static partial Regex GoodName();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotAWord();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();

    public static IEnumerable<McpCustomTool> Discover(IActionDescriptorCollectionProvider actions)
        => actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(action => ToolAttribute.Exposes(action.MethodInfo, action.ControllerTypeInfo))
            .Select(Build);

    private static McpCustomTool Build(ControllerActionDescriptor action)
    {
        var where = $"{action.ControllerTypeInfo.Name}.{action.MethodInfo.Name}";
        if (action.AttributeRouteInfo?.Template is not { } template)
            throw new InvalidOperationException($"[Tool] on {where} needs an attribute route: a tool is a request to one.");

        var pattern = RoutePatternFactory.Parse(template);
        var method = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().FirstOrDefault()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Post;
        var attribute = action.MethodInfo.GetCustomAttribute<ToolAttribute>(inherit: false);
        var name = attribute?.Name ?? Derive(pattern, attribute?.Particle ?? method);
        if (!GoodName().IsMatch(name))
            throw new InvalidOperationException($"The tool name '{name}' of {where} is not lower-case words joined by underscores, at most 64 characters. State one with [Tool(Name = ...)].");

        var (schema, query) = Input(action, pattern, method, where);
        var isGet = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        var description = action.MethodInfo.GetCustomAttribute<DescriptionAttribute>()?.Description;
        return McpCustomToolRegistry.Assemble(name, description ?? $"{method} /{template}", schema, action.MethodInfo, !isGet,
            readOnly: isGet ? true : null, destructive: HttpMethods.IsDelete(method) ? true : null,
            gateAction: EntityAuthorization.ActionOf(method), route: new RouteTool(action, method, pattern, query));
    }

    /// <summary>The route's literal segments, then the particle or HTTP method: <c>/data/posts/{id}</c> with <c>getById</c> makes
    /// <c>data_posts_get_by_id</c>.</summary>
    private static string Derive(RoutePattern pattern, string particle)
    {
        var words = pattern.PathSegments.SelectMany(segment => segment.Parts).OfType<RoutePatternLiteralPart>().Select(part => part.Content).Append(particle);
        return string.Join('_', words.Select(Words).Where(word => word.Length > 0));
    }

    private static string Words(string text)
        => NotAWord().Replace(WordBoundary().Replace(text, "_").ToLowerInvariant(), "_").Trim('_');

    private static (JObject Schema, IReadOnlySet<string> Query) Input(ControllerActionDescriptor action, RoutePattern pattern, string method, string where)
    {
        var properties = new JObject();
        var required = new JArray();
        var query = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var routed = pattern.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        var bodiless = isRead || HttpMethods.IsDelete(method);
        bool AsQuery(BindingSource? source) => source == BindingSource.Query || (bodiless && source != BindingSource.Body);

        void Field(string name, Type type, ICustomAttributeProvider[] sources, bool isRequired, bool asQuery)
        {
            var schema = McpCustomToolRegistry.BuildParameterSchema(type);
            var described = sources.SelectMany(source => source.GetCustomAttributes(typeof(DescriptionAttribute), true)).OfType<DescriptionAttribute>().FirstOrDefault();
            if (described is not null) schema["description"] = described.Description;
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
            if (routed.TryGetValue(parameter.Name, out var route))
                Field(route.Name, parameter.ParameterType, attributes, !route.IsOptional, asQuery: false);
            else if (!IsComplex(parameter.ParameterType))
                Field(Camel(parameter.Name), parameter.ParameterType, attributes, parameter.ParameterInfo.IsDefined(typeof(RequiredAttribute), true), AsQuery(source));
            else
            {
                var constructor = parameter.ParameterType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                foreach (var property in parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
                {
                    var argument = constructor?.GetParameters().FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                    ICustomAttributeProvider[] sources = argument is null ? [property] : [property, argument];
                    Field(Camel(property.Name), property.PropertyType, sources, sources.Any(s => s.IsDefined(typeof(RequiredAttribute), true)), AsQuery(source));
                }
            }
        }

        foreach (var parameter in pattern.Parameters.Where(parameter => !properties.ContainsKey(parameter.Name)))
            Field(parameter.Name, typeof(string), [], !parameter.IsOptional, asQuery: false);

        // Mutating verbs advertise a rehearsal as every custom verb does; the handler answers it without running the action.
        if (!isRead)
            properties[McpDryRun.ArgumentName] = new JObject { ["type"] = "boolean", ["description"] = McpDryRun.SchemaDescription };

        var result = new JObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0) result["required"] = required;
        return (result, query);
    }

    /// <summary>What MVC binds property by property rather than from one value.</summary>
    private static bool IsComplex(Type type)
        => !TypeDescriptor.GetConverter(Nullable.GetUnderlyingType(type) ?? type).CanConvertFrom(typeof(string))
           && !(typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string));

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
