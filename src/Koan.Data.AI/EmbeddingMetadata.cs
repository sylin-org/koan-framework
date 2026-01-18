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
/// Runtime metadata cache for [Embedding] attributes.
/// Provides efficient access to embedding configuration and text generation logic.
/// </summary>
public class EmbeddingMetadata
{
    private static readonly ConcurrentDictionary<Type, EmbeddingMetadata> _cache = new();
    private static readonly ILogger? _logger = (Koan.Core.Hosting.App.AppHost.Current?.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger("Koan.Data.AI.EmbeddingMetadata");

    // Approximate tokens per character for English text (tiktoken-style estimation)
    // Real tokenizers vary, but ~4 chars/token is a reasonable heuristic
    private const double CHARS_PER_TOKEN = 4.0;

    public EmbeddingPolicy Policy { get; init; }
    public string? Template { get; init; }
    public string[] Properties { get; init; } = Array.Empty<string>();
    public bool Async { get; init; }
    public string? Model { get; init; }
    public int BatchSize { get; init; }
    public int RateLimitPerMinute { get; init; }
    public string? Source { get; init; }
    public int MaxTokens { get; init; }
    public int MaxDepth { get; init; }
    public string[] Exclude { get; init; } = Array.Empty<string>();
    public bool WarnOnTruncation { get; init; }
    public int Version { get; init; }

    /// <summary>
    /// Gets cached metadata for the specified entity type.
    /// Thread-safe, cached after first access.
    /// </summary>
    public static EmbeddingMetadata Get(Type entityType)
    {
        return _cache.GetOrAdd(entityType, BuildMetadata);
    }

    /// <summary>
    /// Gets cached metadata for the specified entity type (generic).
    /// </summary>
    public static EmbeddingMetadata Get<TEntity>() where TEntity : class
    {
        return Get(typeof(TEntity));
    }

    private static EmbeddingMetadata BuildMetadata(Type entityType)
    {
        var attr = entityType.GetCustomAttribute<EmbeddingAttribute>();
        if (attr == null)
        {
            throw new InvalidOperationException(
                $"Type {entityType.Name} has no [Embedding] attribute. " +
                $"Add [Embedding] to enable automatic embedding generation.");
        }

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
            Exclude = attr.Exclude ?? Array.Empty<string>(),
            WarnOnTruncation = attr.WarnOnTruncation,
            Version = attr.Version
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

    /// <summary>
    /// Builds embedding text from properties (legacy behavior).
    /// </summary>
    private string BuildFromProperties(object entity)
    {
        var parts = new List<string>();
        var entityType = entity.GetType();

        foreach (var propName in Properties)
        {
            // Skip excluded properties
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

    /// <summary>
    /// Serializes entity to JSON for FullJson policy.
    /// Respects MaxDepth, Exclude, and [EmbeddingIgnore] attributes.
    /// </summary>
    private string SerializeToJson(object entity)
    {
        // MaxDepth in [Embedding] means "nested object depth", but JsonSerializer counts from root
        // Add 2 to account for root object + properties (MaxDepth=2 means allow 2 levels of nested objects)
        var jsonMaxDepth = MaxDepth > 0 ? MaxDepth + 2 : 64;

        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = jsonMaxDepth,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        // Apply property exclusions if specified
        if (Exclude != null && Exclude.Length > 0)
        {
            options.TypeInfoResolver = new ExclusionTypeInfoResolver(Exclude);
        }

        var json = JsonSerializer.Serialize(entity, entity.GetType(), options);
        return json;
    }

    /// <summary>
    /// Custom TypeInfoResolver that excludes specified properties from serialization.
    /// </summary>
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
                // Remove excluded properties
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

    /// <summary>
    /// Truncates text to fit within token limit.
    /// Uses approximate token estimation (chars / 4).
    /// Preserves word boundaries and adds ellipsis.
    /// </summary>
    private string TruncateToTokenLimit(string text, Type entityType)
    {
        var estimatedTokens = EstimateTokens(text);

        if (estimatedTokens <= MaxTokens)
            return text; // No truncation needed

        // Truncate to approximate character limit (reserve 3 chars for "...")
        var maxChars = (int)(MaxTokens * CHARS_PER_TOKEN) - 3;
        if (maxChars <= 0)
            return "...";

        if (text.Length <= maxChars)
            return text;

        // Find last word boundary (space) before maxChars
        var truncated = text.Substring(0, maxChars);
        var lastSpace = truncated.LastIndexOf(' ');

        if (lastSpace > 0)
        {
            truncated = truncated.Substring(0, lastSpace);
        }

        // Add ellipsis (without space before it)
        truncated = truncated.TrimEnd() + "...";

        // Emit warning if configured
        if (WarnOnTruncation && _logger != null)
        {
            _logger.LogWarning(
                "Embedding text truncated for {EntityType}: {EstimatedTokens} tokens > {MaxTokens} limit. " +
                "Consider increasing MaxTokens or simplifying content structure.",
                entityType.Name, estimatedTokens, MaxTokens);
        }

        return truncated;
    }

    /// <summary>
    /// Estimates token count from text length.
    /// Uses approximate heuristic: tokens ≈ chars / 4
    /// </summary>
    public static int EstimateTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / CHARS_PER_TOKEN);
    }

    /// <summary>
    /// Renders template string with property values substituted.
    /// Template format: "{PropertyName}" gets replaced with property value.
    /// String arrays are automatically joined with ", ".
    /// Respects Exclude list.
    /// </summary>
    private string RenderTemplate(object entity)
    {
        var result = Template!;
        var entityType = entity.GetType();

        // Replace {PropertyName} with property values
        foreach (var propName in Properties)
        {
            // Skip excluded properties
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

    /// <summary>
    /// Computes SHA256 content signature from entity's embedding text.
    /// Includes version number to force re-embedding when schema changes.
    /// Same content + same version = same signature = no need to regenerate embedding.
    /// </summary>
    public string ComputeSignature(object entity)
    {
        var text = BuildEmbeddingText(entity);
        // Include version in signature to invalidate cache when schema changes
        var versionedContent = $"v{Version}:{text}";
        return ComputeSignature(versionedContent);
    }

    /// <summary>
    /// Computes SHA256 hash of text content.
    /// </summary>
    public static string ComputeSignature(string text)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Extracts property names from template string.
    /// Example: "{Title}\n\n{Content}" returns ["Title", "Content"]
    /// </summary>
    private static string[] ExtractTemplateProperties(string template)
    {
        var regex = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);
        var matches = regex.Matches(template);
        return matches.Select(m => m.Groups[1].Value).Distinct().ToArray();
    }

    /// <summary>
    /// Infers properties from entity type based on policy.
    /// </summary>
    private static string[] InferPropertiesFromPolicy(Type entityType, EmbeddingPolicy policy)
    {
        return policy switch
        {
            EmbeddingPolicy.AllStrings => entityType.GetProperties()
                .Where(p => p.CanRead && p.GetCustomAttribute<EmbeddingIgnoreAttribute>() == null)
                .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(string[]))
                .Select(p => p.Name)
                .ToArray(),

            EmbeddingPolicy.AllPublic => entityType.GetProperties()
                .Where(p => p.CanRead && p.GetCustomAttribute<EmbeddingIgnoreAttribute>() == null)
                .Select(p => p.Name)
                .ToArray(),

            EmbeddingPolicy.FullJson => Array.Empty<string>(), // Properties not used in FullJson mode

            EmbeddingPolicy.Explicit => throw new InvalidOperationException(
                $"Type {entityType.Name} uses EmbeddingPolicy.Explicit but does not specify Properties or Template. " +
                $"Either set Properties = new[] {{ ... }} or Template = \"...\" in [Embedding] attribute."),

            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy,
                $"Unsupported EmbeddingPolicy: {policy}")
        };
    }
}
