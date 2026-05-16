using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Koan.Data.AI.Attributes;
using Microsoft.Extensions.Logging;

namespace Koan.Data.AI;

/// <summary>
/// Runtime metadata cache for embedding configuration.
/// Convention-first: Resolve() never returns null — decorated entities get attribute config,
/// undecorated entities get convention-inferred config (AllStrings policy, lifecycle disabled).
/// </summary>
public class EmbeddingMetadata
{
    private static readonly ConcurrentDictionary<Type, EmbeddingMetadata> _cache = new();
    private static readonly ILogger? _logger = (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger("Koan.Data.AI.EmbeddingMetadata");

    // Approximate tokens per character for English text (tiktoken-style estimation)
    private const double CHARS_PER_TOKEN = 4.0;

    // Entity infrastructure properties excluded from convention/AllStrings scanning.
    // These are identity/framework fields, not semantic content.
    private static readonly HashSet<string> InfrastructureProperties = new(StringComparer.OrdinalIgnoreCase) { "Id" };

    public EmbeddingPolicy Policy { get; init; }
    public string? Template { get; init; }
    public string[] Properties { get; init; } = [];
    public bool Async { get; init; }
    public string? Model { get; init; }
    public int BatchSize { get; init; }
    public int RateLimitPerMinute { get; init; }
    public string? Source { get; init; }
    public int MaxTokens { get; init; }
    public int MaxDepth { get; init; }
    public string[] Exclude { get; init; } = [];
    public bool WarnOnTruncation { get; init; }
    public int Version { get; init; }

    /// <summary>
    /// True when [Embedding] attribute is present — gates auto-embed-on-save lifecycle hooks.
    /// Convention-inferred metadata has this set to false.
    /// </summary>
    public bool LifecycleEnabled { get; init; }

    /// <summary>
    /// True when the entity type has [Embedding] attribute.
    /// </summary>
    public bool HasAttribute { get; init; }

    /// <summary>
    /// Resolves metadata for the specified entity type. Never returns null.
    /// Decorated entities get attribute-configured metadata; undecorated entities
    /// get convention-inferred metadata (AllStrings policy, no lifecycle).
    /// </summary>
    public static EmbeddingMetadata Resolve(Type entityType)
    {
        return _cache.GetOrAdd(entityType, BuildMetadata);
    }

    /// <summary>
    /// Resolves metadata for the specified entity type (generic). Never returns null.
    /// </summary>
    public static EmbeddingMetadata Resolve<TEntity>() where TEntity : class
    {
        return Resolve(typeof(TEntity));
    }

    private static EmbeddingMetadata BuildMetadata(Type entityType)
    {
        var attr = entityType.GetCustomAttribute<EmbeddingAttribute>();
        if (attr is not null)
        {
            return BuildFromAttribute(entityType, attr);
        }

        return InferConvention(entityType);
    }

    private static EmbeddingMetadata BuildFromAttribute(Type entityType, EmbeddingAttribute attr)
    {
        string[] properties;

        // Precedence: Template > Properties > Policy
        if (attr.Template != null)
        {
            properties = ExtractTemplateProperties(attr.Template);
        }
        else if (attr.Properties != null && attr.Properties.Length > 0)
        {
            properties = attr.Properties;
        }
        else
        {
            properties = InferPropertiesFromPolicy(entityType, attr.Policy);
        }

        return new EmbeddingMetadata
        {
            Policy = attr.Policy,
            Template = attr.Template,
            Properties = properties,
            Async = attr.Async,
            Model = attr.Model,
            BatchSize = attr.BatchSize,
            RateLimitPerMinute = attr.RateLimitPerMinute,
            Source = attr.Source,
            MaxTokens = attr.MaxTokens,
            MaxDepth = attr.MaxDepth,
            Exclude = attr.Exclude ?? [],
            WarnOnTruncation = attr.WarnOnTruncation,
            Version = attr.Version,
            LifecycleEnabled = true,
            HasAttribute = true
        };
    }

    /// <summary>
    /// Infers convention metadata for entities without [Embedding] attribute.
    /// Scans public string properties (excluding [EmbeddingIgnore]).
    /// Lifecycle disabled — on-demand only.
    /// </summary>
    private static EmbeddingMetadata InferConvention(Type entityType)
    {
        var properties = entityType.GetProperties()
            .Where(p => p.CanRead && p.GetCustomAttribute<EmbeddingIgnoreAttribute>() == null)
            .Where(p => !InfrastructureProperties.Contains(p.Name))
            .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(string[]))
            .Select(p => p.Name)
            .ToArray();

        return new EmbeddingMetadata
        {
            Policy = EmbeddingPolicy.AllStrings,
            Properties = properties,
            LifecycleEnabled = false,
            HasAttribute = false
        };
    }

    /// <summary>
    /// Builds embedding text from entity based on configured properties/template.
    /// Applies token truncation if MaxTokens > 0.
    /// </summary>
    public string BuildEmbeddingText(object entity)
    {
        string text;

        if (Policy == EmbeddingPolicy.FullJson)
        {
            text = SerializeToJson(entity);
        }
        else if (Template != null)
        {
            text = RenderTemplate(entity);
        }
        else
        {
            text = BuildFromProperties(entity);
        }

        // Apply token truncation if configured
        if (MaxTokens > 0)
        {
            text = TruncateToTokenLimit(text, entity.GetType());
        }

        return text;
    }

    private string BuildFromProperties(object entity)
    {
        var parts = new List<string>();
        var entityType = entity.GetType();

        foreach (var propName in Properties)
        {
            if (Exclude.Contains(propName))
                continue;

            var prop = entityType.GetProperty(propName);
            if (prop == null) continue;

            var value = prop.GetValue(entity);
            if (value == null) continue;

            if (value is string str && !string.IsNullOrWhiteSpace(str))
            {
                parts.Add(str);
            }
            else if (value is IEnumerable<string> array)
            {
                var items = array.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (items.Count > 0)
                {
                    parts.Add(string.Join(", ", items));
                }
            }
        }

        return string.Join("\n\n", parts);
    }

    private string SerializeToJson(object entity)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 64,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        if (Exclude != null && Exclude.Length > 0)
        {
            options.TypeInfoResolver = new ExclusionTypeInfoResolver(Exclude);
        }

        var json = JsonSerializer.Serialize(entity, entity.GetType(), options);

        if (MaxDepth > 0)
        {
            json = TruncateJsonDepth(json);
        }

        return json;
    }

    private string TruncateJsonDepth(string json)
    {
        using var doc = JsonDocument.Parse(json);
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        WriteWithDepthLimit(writer, doc.RootElement, 0);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteWithDepthLimit(Utf8JsonWriter writer, JsonElement element, int objectDepth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (objectDepth > MaxDepth)
                {
                    writer.WriteNullValue();
                    return;
                }
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteWithDepthLimit(writer, property.Value, objectDepth + 1);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteWithDepthLimit(writer, item, objectDepth);
                }
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private class ExclusionTypeInfoResolver : System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
    {
        private readonly HashSet<string> _excludedProperties;

        public ExclusionTypeInfoResolver(string[] excludedProperties)
        {
            _excludedProperties = new HashSet<string>(excludedProperties, StringComparer.OrdinalIgnoreCase);
        }

        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var typeInfo = base.GetTypeInfo(type, options);

            if (typeInfo.Kind == System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
            {
                var propsToRemove = typeInfo.Properties
                    .Where(p => _excludedProperties.Contains(p.Name))
                    .ToList();

                foreach (var prop in propsToRemove)
                {
                    typeInfo.Properties.Remove(prop);
                }
            }

            return typeInfo;
        }
    }

    private string TruncateToTokenLimit(string text, Type entityType)
    {
        var estimatedTokens = EstimateTokens(text);

        if (estimatedTokens <= MaxTokens)
            return text;

        var maxChars = (int)(MaxTokens * CHARS_PER_TOKEN) - 3;
        if (maxChars < 4)
            return "...";

        if (text.Length <= maxChars)
            return text;

        var truncated = text.Substring(0, maxChars);
        var lastSpace = truncated.LastIndexOf(' ');

        if (lastSpace > 0)
        {
            truncated = truncated.Substring(0, lastSpace);
        }

        truncated = truncated.TrimEnd() + "...";

        if (WarnOnTruncation && _logger != null)
        {
            _logger.LogWarning(
                "Embedding text truncated for {EntityType}: {EstimatedTokens} tokens > {MaxTokens} limit. " +
                "Consider increasing MaxTokens or simplifying content structure.",
                entityType.Name, estimatedTokens, MaxTokens);
        }

        return truncated;
    }

    public static int EstimateTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / CHARS_PER_TOKEN);
    }

    private string RenderTemplate(object entity)
    {
        var result = Template!;
        var entityType = entity.GetType();

        foreach (var propName in Properties)
        {
            if (Exclude.Contains(propName))
            {
                result = result.Replace($"{{{propName}}}", "");
                continue;
            }

            var prop = entityType.GetProperty(propName);
            if (prop == null) continue;

            var value = prop.GetValue(entity);
            string replacement = "";

            if (value is string str)
            {
                replacement = str;
            }
            else if (value is IEnumerable<string> array)
            {
                var items = array.Where(s => !string.IsNullOrWhiteSpace(s));
                replacement = string.Join(", ", items);
            }
            else if (value != null)
            {
                replacement = value.ToString() ?? "";
            }

            result = result.Replace($"{{{propName}}}", replacement);
        }

        return result;
    }

    public string ComputeSignature(object entity)
    {
        var text = BuildEmbeddingText(entity);
        var versionedContent = $"v{Version}:{text}";
        return ComputeSignature(versionedContent);
    }

    public static string ComputeSignature(string text)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string[] ExtractTemplateProperties(string template)
    {
        var regex = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);
        var matches = regex.Matches(template);
        return matches.Select(m => m.Groups[1].Value).Distinct().ToArray();
    }

    private static string[] InferPropertiesFromPolicy(Type entityType, EmbeddingPolicy policy)
    {
        return policy switch
        {
            EmbeddingPolicy.AllStrings => entityType.GetProperties()
                .Where(p => p.CanRead && p.GetCustomAttribute<EmbeddingIgnoreAttribute>() == null)
                .Where(p => !InfrastructureProperties.Contains(p.Name))
                .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(string[]))
                .Select(p => p.Name)
                .ToArray(),

            EmbeddingPolicy.AllPublic => entityType.GetProperties()
                .Where(p => p.CanRead && p.GetCustomAttribute<EmbeddingIgnoreAttribute>() == null)
                .Where(p => !InfrastructureProperties.Contains(p.Name))
                .Select(p => p.Name)
                .ToArray(),

            EmbeddingPolicy.FullJson => [],

            EmbeddingPolicy.Explicit => throw new InvalidOperationException(
                $"Type {entityType.Name} uses EmbeddingPolicy.Explicit but does not specify Properties or Template. " +
                $"Either set Properties = new[] {{ ... }} or Template = \"...\" in [Embedding] attribute."),

            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy,
                $"Unsupported EmbeddingPolicy: {policy}")
        };
    }
}
