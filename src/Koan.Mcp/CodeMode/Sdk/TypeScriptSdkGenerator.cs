using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Koan.Web.Endpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Mcp.CodeMode.Sdk;

/// <summary>
/// Generates TypeScript SDK definitions (.d.ts) from entity metadata.
/// These definitions guide LLM code generation but are not executed.
/// </summary>
public sealed partial class TypeScriptSdkGenerator
{
    private readonly ILogger<TypeScriptSdkGenerator> _logger;
    private readonly IOptionsMonitor<TypeScriptSdkOptions> _options;

    public TypeScriptSdkGenerator(ILogger<TypeScriptSdkGenerator> logger, IOptionsMonitor<TypeScriptSdkOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
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

        var content = sb.ToString();
        PersistIfEnabled(content);
        return content;
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

        // Emit union types for sets and relationships if available
        var meta = registration.Descriptor.Metadata;
        if (meta.AvailableSets is { Count: > 0 })
        {
            var setLiterals = string.Join(" | ", meta.AvailableSets.Select(s => $"\"{s}\""));
            sb.AppendLine($"    type {entityName}Set = {setLiterals};");
        }
        if (meta.RelationshipNames is { Count: > 0 })
        {
            var relLiterals = string.Join(" | ", meta.RelationshipNames.Select(r => $"\"{r}\""));
            sb.AppendLine($"    type {entityName}Relationship = {relLiterals} | \"all\";");
        }
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

        var allowMutations = registration.Attribute.AllowMutations != false;
        foreach (var tool in registration.Tools.OrderBy(t => t.Operation))
        {
            if (!allowMutations && tool.IsMutation) continue; // omit mutation signatures when not allowed
            var signature = GenerateMethodSignature(registration, entityName, tool);
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

    private string GenerateMethodSignature(McpEntityRegistration registration, string entityName, McpToolDefinition tool)
    {
        // Determine if union types were emitted for this entity by checking registration metadata
        var metadata = registration.Descriptor.Metadata;
        var hasSetUnion = metadata?.AvailableSets is { Count: > 0 };
        var hasRelUnion = metadata?.RelationshipNames is { Count: > 0 };
        string SetParam(string name)
            => hasSetUnion ? $"{name}?: {entityName}Set" : $"{name}?: string";
        string RelParam(string name)
            => hasRelUnion ? $"{name}?: {entityName}Relationship" : $"{name}?: string";

        return tool.Operation switch
        {
            EntityEndpointOperationKind.Collection =>
                $"collection(params?: {{ filter?: any; pageSize?: number; page?: number; sort?: string; {SetParam("set")}; {RelParam("with")} }}): {{ items: {entityName}[]; page: number; pageSize: number; totalCount: number }}",

            EntityEndpointOperationKind.GetById =>
                $"getById(id: string, options?: {{ {SetParam("set")}; {RelParam("with")} }}): {entityName}",

            EntityEndpointOperationKind.Upsert =>
                $"upsert(model: {entityName}, options?: {{ {SetParam("set")} }}): {entityName}",

            EntityEndpointOperationKind.Delete =>
                $"delete(id: string, options?: {{ {SetParam("set")} }}): number",

            EntityEndpointOperationKind.DeleteMany =>
                $"deleteMany(ids: string[], options?: {{ {SetParam("set")} }}): number",

            _ => ""
        };
    }

    private List<(string Name, string Type, string? Description)> ExtractEntityProperties(JObject? schema)
    {
        var properties = new List<(string, string, string?)>();

        if (schema == null) return properties;

        // Navigate to model properties for upsert
        if (schema.TryGetValue("properties", StringComparison.OrdinalIgnoreCase, out var propsNode) && propsNode is JObject propsObj)
        {
            // Check if there's a "model" property (upsert schema)
            if (propsObj.TryGetValue("model", StringComparison.OrdinalIgnoreCase, out var modelNode) && modelNode is JObject modelObj &&
                modelObj.TryGetValue("properties", StringComparison.OrdinalIgnoreCase, out var modelPropsNode) && modelPropsNode is JObject modelProps)
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

    private void ExtractPropertiesFromJsonSchema(JObject schemaProps, List<(string, string, string?)> properties)
    {
        foreach (var prop in schemaProps.Properties())
        {
            var propName = prop.Name;
            var propSchema = prop.Value as JObject;
            if (propSchema == null) continue;

            var propType = MapJsonSchemaToTypeScript(propSchema);
            var descNode = propSchema.GetValue("description");
            var propDesc = descNode?.Type == JTokenType.String ? descNode.Value<string>() : null;

            properties.Add((propName, propType, propDesc));
        }
    }

    private string MapJsonSchemaToTypeScript(JObject? schema)
    {
        if (schema == null) return "any";

        if (!schema.TryGetValue("type", StringComparison.OrdinalIgnoreCase, out var typeNode))
        {
            return "any";
        }

        var type = typeNode?.Value<string>();

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

    private string MapStringType(JObject schema)
    {
        if (schema.TryGetValue("enum", StringComparison.OrdinalIgnoreCase, out var enumNode) && enumNode is JArray enumArray)
        {
            var values = enumArray.Select(v => v.Type == JTokenType.String ? $"\"{v.Value<string>()}\"" : null).Where(v => !string.IsNullOrEmpty(v));
            return string.Join(" | ", values);
        }

        if (schema.TryGetValue("format", StringComparison.OrdinalIgnoreCase, out var formatNode))
        {
            var format = formatNode?.Value<string>();
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

    private string MapArrayType(JObject schema)
    {
        if (schema.TryGetValue("items", StringComparison.OrdinalIgnoreCase, out var itemsNode) && itemsNode is JObject itemsSchema)
        {
            var itemType = MapJsonSchemaToTypeScript(itemsSchema);
            return $"{itemType}[]";
        }

        return "any[]";
    }
}

/// <summary>
/// Options controlling TypeScript definition generation.
/// </summary>
public sealed class TypeScriptSdkOptions
{
    /// <summary>
    /// Enable writing the generated .d.ts file to disk for inspection.
    /// </summary>
    public bool WriteFile { get; set; } = true;

    /// <summary>
    /// Relative path (under application base) to write file when enabled.
    /// </summary>
    public string OutputPath { get; set; } = "mcp-sdk/koan-code-mode.d.ts";
}

/// <summary>
/// Caches the generated TypeScript SDK so other components (e.g. diagnostics endpoint) can serve it.
/// </summary>
public interface ITypeScriptSdkProvider
{
    string? Current { get; }
}

internal sealed class TypeScriptSdkProvider : ITypeScriptSdkProvider
{
    public string? Current { get; internal set; }
}

internal static class TypeScriptSdkGeneratorExtensions
{
    public static void AddTypeScriptSdk(this IServiceCollection services)
    {
        services.AddSingleton<TypeScriptSdkGenerator>();
        services.AddSingleton<TypeScriptSdkProvider>();
        services.AddSingleton<ITypeScriptSdkProvider>(sp => sp.GetRequiredService<TypeScriptSdkProvider>());
    }
}

partial class TypeScriptSdkGenerator
{
    private void PersistIfEnabled(string content)
    {
        try
        {
            var opts = _options.CurrentValue;
            if (!opts.WriteFile) return;

            var path = Path.GetFullPath(opts.OutputPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir!);
            }

            // Compute hash of the logical content (without footer if already present)
            var normalized = StripExistingFooter(content);
            var hash = ComputeSha256(normalized);
            var hashedBytesLength = Encoding.UTF8.GetByteCount(normalized);
            var footer = $"// integrity-sha256: {hash}";
            if (!normalized.EndsWith("\n")) normalized += "\n"; // ensure newline before footer
            var finalContent = normalized + footer + "\n";

            // Skip write if existing file already has same footer hash
            if (File.Exists(path))
            {
                try
                {
                    var existing = File.ReadAllText(path, Encoding.UTF8);
                    var existingHash = ExtractFooterHash(existing);
                    if (existingHash == hash)
                    {
                        // Provide richer diagnostics for no-op case
                        _logger.LogInformation("TypeScript SDK unchanged (hash {Hash}, length {Length} chars, hashed-bytes {HashedBytes}); skipping write", hash, existing.Length, hashedBytesLength);
                        return;
                    }
                }
                catch (Exception readEx)
                {
                    _logger.LogDebug(readEx, "Ignoring read failure when checking existing SDK file");
                }
            }

            const int maxAttempts = 3;
            var delayMs = 25;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    File.WriteAllText(path, finalContent, Encoding.UTF8);
                    _logger.LogInformation("TypeScript SDK definitions written to {Path} ({Length} chars, hashed-bytes {HashedBytes}, hash {Hash})", path, finalContent.Length, hashedBytesLength, hash);
                    return;
                }
                catch (IOException ioEx) when (attempt < maxAttempts)
                {
                    _logger.LogWarning(ioEx, "Attempt {Attempt}/{Max} to write TypeScript SDK failed due to IO. Retrying in {Delay}ms", attempt, maxAttempts, delayMs);
                    System.Threading.Thread.Sleep(delayMs);
                    delayMs *= 3; // simple backoff
                }
            }
            _logger.LogWarning("Exhausted retries writing TypeScript SDK to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist TypeScript SDK definitions");
        }
    }

    private static string StripExistingFooter(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        // Normalize to LF-only for deterministic hashing across platforms
        var cleaned = content.Replace("\r", string.Empty);
        var lines = cleaned.Split('\n');
        if (lines.Length == 0) return string.Empty;
        var last = lines[^1].Trim();
        if (last.StartsWith("// integrity-sha256:"))
        {
            return string.Join('\n', lines.Take(lines.Length - 1));
        }
        return cleaned;
    }

    private static string? ExtractFooterHash(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var lines = content.Replace("\r", string.Empty).Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("// integrity-sha256:"))
            {
                var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    return parts[1].Trim();
                }
            }
            if (!string.IsNullOrWhiteSpace(line)) break; // stop scanning after first non-empty non-footer line
        }
        return null;
    }

    private static string ComputeSha256(string content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = sha.ComputeHash(bytes);
        return string.Concat(hashBytes.Select(b => b.ToString("x2")));
    }
}
