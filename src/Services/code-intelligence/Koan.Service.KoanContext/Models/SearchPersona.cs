using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Search persona defines default weights, boosts, and tag preferences for a channel or user intent.
/// </summary>
public class SearchPersona : Entity<SearchPersona>
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float SemanticWeight { get; set; } = 0.6f;
    public float TagWeight { get; set; } = 0.3f;
    public float RecencyWeight { get; set; } = 0.1f;
    public int MaxTokens { get; set; } = 6000;
    public bool IncludeInsights { get; set; } = true;
    public bool IncludeReasoning { get; set; } = true;
    public Dictionary<string, float> TagBoosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DefaultTagsAny { get; set; } = new();
    public List<string> DefaultTagsAll { get; set; } = new();
    public List<string> DefaultTagsExclude { get; set; } = new();
    public bool IsActive { get; set; } = true;

    public static SearchPersona Create(
        string name,
        string displayName,
        string description,
        float semanticWeight,
        float tagWeight,
        float recencyWeight,
        int maxTokens,
        IDictionary<string, float>? tagBoosts = null,
        IEnumerable<string>? defaultAny = null,
        IEnumerable<string>? defaultAll = null,
        IEnumerable<string>? defaultExclude = null,
        bool includeInsights = true,
        bool includeReasoning = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Persona name is required", nameof(name));

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required", nameof(displayName));

        if (maxTokens < 1000 || maxTokens > 20000)
            throw new ArgumentException("MaxTokens must be between 1000 and 20000", nameof(maxTokens));

        return new SearchPersona
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            SemanticWeight = Clamp01(semanticWeight),
            TagWeight = Clamp01(tagWeight),
            RecencyWeight = Clamp01(recencyWeight),
            MaxTokens = maxTokens,
            TagBoosts = new Dictionary<string, float>(NormalizeBoosts(tagBoosts), StringComparer.OrdinalIgnoreCase),
            DefaultTagsAny = TagEnvelope.NormalizeTags(defaultAny).ToList(),
            DefaultTagsAll = TagEnvelope.NormalizeTags(defaultAll).ToList(),
            DefaultTagsExclude = TagEnvelope.NormalizeTags(defaultExclude).ToList(),
            IncludeInsights = includeInsights,
            IncludeReasoning = includeReasoning,
            IsActive = true
        };
    }

    public IReadOnlyDictionary<string, float> GetTagBoosts()
        => TagBoosts;

    public IReadOnlyList<string> GetDefaultTagsAny()
        => DefaultTagsAny;

    public IReadOnlyList<string> GetDefaultTagsAll()
        => DefaultTagsAll;

    public IReadOnlyList<string> GetDefaultTagsExclude()
        => DefaultTagsExclude;

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static IDictionary<string, float> NormalizeBoosts(IDictionary<string, float>? boosts)
    {
        if (boosts == null)
        {
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        return boosts
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
            .ToDictionary(
                kvp => kvp.Key.Trim().ToLowerInvariant(),
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase);
    }
}
