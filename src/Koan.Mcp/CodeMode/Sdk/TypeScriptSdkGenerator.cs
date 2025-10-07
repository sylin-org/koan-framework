using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Koan.Web.Endpoints;

namespace Koan.Mcp.CodeMode.Sdk;

/// <summary>
/// Generates TypeScript SDK definitions (.d.ts) from entity metadata.
/// These definitions guide LLM code generation but are not executed.
/// </summary>
public sealed class TypeScriptSdkGenerator
{
    /// <summary>
    /// Generate TypeScript definitions for all registered entities.
    /// </summary>
    public string GenerateDefinitions(IEnumerable<McpEntityRegistration> registrations)
    {
        if (registrations == null) throw new ArgumentNullException(nameof(registrations));

        var sb = new StringBuilder();

        // Header
        sb.AppendLine("// Auto-generated TypeScript definitions for Koan MCP Code Mode");
        sb.AppendLine($"// Generated: {DateTime.UtcNow:O}");
        sb.AppendLine("// Use these type definitions to guide code generation.");
        sb.AppendLine("// Code execution is JavaScript-only (no TypeScript transpilation).");
        sb.AppendLine();

        sb.AppendLine("declare namespace Koan {");
        sb.AppendLine("  // ──────────────────────────────────────────────────");
        sb.AppendLine("  // Entity Domain - Auto-discovered entity operations");
        sb.AppendLine("  // ──────────────────────────────────────────────────");
        sb.AppendLine("  namespace Entities {");

        var sortedRegistrations = registrations.OrderBy(r => r.DisplayName).ToList();

        foreach (var reg in sortedRegistrations)
        {
            GenerateEntityDefinitions(sb, reg);
        }

        sb.AppendLine("  }");
        sb.AppendLine();

        // Output domain
        sb.AppendLine("  // ──────────────────────────────────────────────────");
        sb.AppendLine("  // Output Domain - Communication with user");
        sb.AppendLine("  // ──────────────────────────────────────────────────");
        sb.AppendLine("  namespace Out {");
        sb.AppendLine("    /** Send final answer to the user */");
        sb.AppendLine("    function answer(text: string): void;");
        sb.AppendLine();
        sb.AppendLine("    /** Log informational message */");
        sb.AppendLine("    function info(message: string): void;");
        sb.AppendLine();
        sb.AppendLine("    /** Log warning message */");
        sb.AppendLine("    function warn(message: string): void;");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();

        // Runtime context
        sb.AppendLine("// ──────────────────────────────────────────────────");
        sb.AppendLine("// Runtime Context - Available to JavaScript code");
        sb.AppendLine("// ──────────────────────────────────────────────────");
        sb.AppendLine("export interface CodeModeContext {");
        sb.AppendLine("  SDK: typeof Koan;");
        sb.AppendLine("}");
        sb.AppendLine();

        return sb.ToString();
    }

    private void GenerateEntityDefinitions(StringBuilder sb, McpEntityRegistration registration)
    {
        var entityName = registration.DisplayName;

        sb.AppendLine();
        sb.AppendLine($"    // {entityName} - {registration.Attribute.Description ?? "Entity operations"}");

        // Generate entity type interface
        GenerateEntityTypeInterface(sb, registration);

        // Generate operations interface
        GenerateOperationsInterface(sb, registration);

        // Declare the entity constant
        sb.AppendLine($"    const {entityName}: I{entityName}Operations;");
    }

    private void GenerateEntityTypeInterface(StringBuilder sb, McpEntityRegistration registration)
    {
        var entityName = registration.DisplayName;

        sb.AppendLine($"    interface {entityName} {{");

        // Extract properties from upsert or collection tool schema
        var propertiesFound = false;

        foreach (var tool in registration.Tools)
        {
            if (tool.Operation is EntityEndpointOperationKind.Upsert or EntityEndpointOperationKind.Collection)
            {
                var props = ExtractEntityProperties(tool.InputSchema);
                if (props.Count > 0)
                {
                    foreach (var (propName, propType, propDesc) in props)
                    {
                        if (!string.IsNullOrWhiteSpace(propDesc))
                        {
                            sb.AppendLine($"      /** {propDesc} */");
                        }
                        sb.AppendLine($"      {propName}: {propType};");
                    }
                    propertiesFound = true;
                    break;
                }
            }
        }

        if (!propertiesFound)
        {
            sb.AppendLine("      id: string;");
            sb.AppendLine("      [key: string]: any;");
        }

        sb.AppendLine($"    }}");
        sb.AppendLine();
    }

    private void GenerateOperationsInterface(StringBuilder sb, McpEntityRegistration registration)
    {
        var entityName = registration.DisplayName;

        sb.AppendLine($"    interface I{entityName}Operations {{");

        foreach (var tool in registration.Tools.OrderBy(t => t.Operation))
        {
            var signature = GenerateMethodSignature(entityName, tool);
            if (!string.IsNullOrWhiteSpace(signature))
            {
                if (!string.IsNullOrWhiteSpace(tool.Description))
                {
                    sb.AppendLine($"      /** {tool.Description} */");
                }
                sb.AppendLine($"      {signature};");
                sb.AppendLine();
            }
        }

        sb.AppendLine($"    }}");
        sb.AppendLine();
    }

    private string GenerateMethodSignature(string entityName, McpToolDefinition tool)
    {
        return tool.Operation switch
        {
            EntityEndpointOperationKind.Collection =>
                $"collection(params?: {{ filter?: any; pageSize?: number; page?: number; sort?: string; set?: string; with?: string }}): {{ items: {entityName}[]; page: number; pageSize: number; totalCount: number }}",

            EntityEndpointOperationKind.GetById =>
                $"getById(id: string, options?: {{ set?: string; with?: string }}): {entityName}",

            EntityEndpointOperationKind.Upsert =>
                $"upsert(model: {entityName}, options?: {{ set?: string }}): {entityName}",

            EntityEndpointOperationKind.Delete =>
                $"delete(id: string, options?: {{ set?: string }}): number",

            EntityEndpointOperationKind.DeleteMany =>
                $"deleteMany(ids: string[], options?: {{ set?: string }}): number",

            _ => ""
        };
    }

    private List<(string Name, string Type, string? Description)> ExtractEntityProperties(JsonObject? schema)
    {
        var properties = new List<(string, string, string?)>();

        if (schema == null) return properties;

        // Navigate to model properties for upsert
        if (schema.TryGetPropertyValue("properties", out var propsNode) &&
            propsNode is JsonObject propsObj)
        {
            // Check if there's a "model" property (upsert schema)
            if (propsObj.TryGetPropertyValue("model", out var modelNode) &&
                modelNode is JsonObject modelObj &&
                modelObj.TryGetPropertyValue("properties", out var modelPropsNode) &&
                modelPropsNode is JsonObject modelProps)
            {
                ExtractPropertiesFromJsonSchema(modelProps, properties);
            }
            else
            {
                // Direct properties (other operations)
                ExtractPropertiesFromJsonSchema(propsObj, properties);
            }
        }

        return properties;
    }

    private void ExtractPropertiesFromJsonSchema(JsonObject schemaProps, List<(string, string, string?)> properties)
    {
        foreach (var prop in schemaProps)
        {
            var propName = prop.Key;
            var propSchema = prop.Value?.AsObject();
            if (propSchema == null) continue;

            var propType = MapJsonSchemaToTypeScript(propSchema);
            var propDesc = propSchema.TryGetPropertyValue("description", out var descNode)
                ? descNode?.GetValue<string>()
                : null;

            properties.Add((propName, propType, propDesc));
        }
    }

    private string MapJsonSchemaToTypeScript(JsonObject? schema)
    {
        if (schema == null) return "any";

        if (!schema.TryGetPropertyValue("type", out var typeNode))
        {
            return "any";
        }

        var type = typeNode?.GetValue<string>();

        return type switch
        {
            "string" => MapStringType(schema),
            "number" => "number",
            "integer" => "number",
            "boolean" => "boolean",
            "array" => MapArrayType(schema),
            "object" => "Record<string, any>",
            _ => "any"
        };
    }

    private string MapStringType(JsonObject schema)
    {
        if (schema.TryGetPropertyValue("enum", out var enumNode) &&
            enumNode is JsonArray enumArray)
        {
            var values = enumArray
                .Select(v => $"\"{v?.GetValue<string>()}\"")
                .Where(v => !string.IsNullOrEmpty(v));
            return string.Join(" | ", values);
        }

        if (schema.TryGetPropertyValue("format", out var formatNode))
        {
            var format = formatNode?.GetValue<string>();
            return format switch
            {
                "date-time" => "string", // ISO 8601
                "uuid" => "string",
                "date" => "string",
                _ => "string"
            };
        }

        return "string";
    }

    private string MapArrayType(JsonObject schema)
    {
        if (schema.TryGetPropertyValue("items", out var itemsNode) &&
            itemsNode is JsonObject itemsSchema)
        {
            var itemType = MapJsonSchemaToTypeScript(itemsSchema);
            return $"{itemType}[]";
        }

        return "any[]";
    }
}
