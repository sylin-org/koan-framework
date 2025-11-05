using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Koan.Data.AI.Attributes;

namespace Koan.Data.AI;

/// <summary>
/// Runtime metadata cache for [Embedding] attributes.
/// Provides efficient access to embedding configuration and text generation logic.
/// </summary>
public class EmbeddingMetadata
{
    private static readonly ConcurrentDictionary<Type, EmbeddingMetadata> _cache = new();

    public EmbeddingPolicy Policy { get; init; }
    public string? Template { get; init; }
    public string[] Properties { get; init; } = Array.Empty<string>();
    public bool Async { get; init; }
    public string? Model { get; init; }
    public int BatchSize { get; init; }
    public int? RateLimitPerMinute { get; init; }

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
            RateLimitPerMinute = attr.RateLimitPerMinute
        };
    }

    /// <summary>
    /// Builds embedding text from entity based on configured properties/template.
    /// </summary>
    public string BuildEmbeddingText(object entity)
    {
        if (Template != null)
        {
            return RenderTemplate(entity);
        }

        var parts = new List<string>();
        var entityType = entity.GetType();

        foreach (var propName in Properties)
        {
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
    /// Renders template string with property values substituted.
    /// Template format: "{PropertyName}" gets replaced with property value.
    /// String arrays are automatically joined with ", ".
    /// </summary>
    private string RenderTemplate(object entity)
    {
        var result = Template!;
        var entityType = entity.GetType();

        // Replace {PropertyName} with property values
        foreach (var propName in Properties)
        {
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
    /// Same content = same signature = no need to regenerate embedding.
    /// </summary>
    public string ComputeSignature(object entity)
    {
        var text = BuildEmbeddingText(entity);
        return ComputeSignature(text);
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

            EmbeddingPolicy.Explicit => throw new InvalidOperationException(
                $"Type {entityType.Name} uses EmbeddingPolicy.Explicit but does not specify Properties or Template. " +
                $"Either set Properties = new[] {{ ... }} or Template = \"...\" in [Embedding] attribute."),

            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy,
                $"Unsupported EmbeddingPolicy: {policy}")
        };
    }
}
