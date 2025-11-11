using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Context.Models;

/// <summary>
/// Channel indicator for search requests.
/// </summary>
public enum SearchChannel
{
    Web,
    Mcp
}

/// <summary>
/// Normalized request context shared between web and MCP search flows.
/// </summary>
public sealed record SearchRequestContext(
    string Query,
    IReadOnlyList<string> ProjectIds,
    string? PathContext,
    IReadOnlyList<string> TagsAny,
    IReadOnlyList<string> TagsAll,
    IReadOnlyList<string> TagsExclude,
    IReadOnlyDictionary<string, float> TagBoosts,
    string? PersonaId,
    SearchChannel Channel,
    string? ContinuationToken,
    int MaxTokens,
    bool IncludeInsights,
    bool IncludeReasoning,
    IReadOnlyList<string>? Languages)
{
    public static SearchRequestContext Create(
        string query,
        IEnumerable<string>? projectIds = null,
        string? pathContext = null,
        IEnumerable<string>? tagsAny = null,
        IEnumerable<string>? tagsAll = null,
        IEnumerable<string>? tagsExclude = null,
        IDictionary<string, float>? tagBoosts = null,
        string? personaId = null,
        SearchChannel channel = SearchChannel.Web,
        string? continuationToken = null,
        int? maxTokens = null,
        bool? includeInsights = null,
        bool? includeReasoning = null,
        IEnumerable<string>? languages = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be empty", nameof(query));
        }

        var sanitizedProjects = projectIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        var sanitizedTagsAny = TagEnvelope.NormalizeTags(tagsAny);
        var sanitizedTagsAll = TagEnvelope.NormalizeTags(tagsAll);
        var sanitizedTagsExclude = TagEnvelope.NormalizeTags(tagsExclude);

        var sanitizedBoosts = tagBoosts == null
            ? FrozenDictionary<string, float>.Empty
            : tagBoosts
                .Where(static kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                .ToDictionary(
                    static kvp => kvp.Key.Trim().ToLowerInvariant(),
                    static kvp => kvp.Value,
                    StringComparer.OrdinalIgnoreCase)
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        var sanitizedLanguages = languages?
            .Where(static lang => !string.IsNullOrWhiteSpace(lang))
            .Select(static lang => lang.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var resolvedMaxTokens = Math.Clamp(maxTokens ?? 6000, 1000, 20000);
        var defaultInsightSetting = channel == SearchChannel.Mcp ? false : true;
        var defaultReasoningSetting = channel == SearchChannel.Mcp ? false : true;
        var resolvedIncludeInsights = includeInsights ?? defaultInsightSetting;
        var resolvedIncludeReasoning = includeReasoning ?? defaultReasoningSetting;

        return new SearchRequestContext(
            Query: query,
            ProjectIds: sanitizedProjects,
            PathContext: string.IsNullOrWhiteSpace(pathContext) ? null : pathContext.Trim(),
            TagsAny: sanitizedTagsAny,
            TagsAll: sanitizedTagsAll,
            TagsExclude: sanitizedTagsExclude,
            TagBoosts: sanitizedBoosts,
            PersonaId: string.IsNullOrWhiteSpace(personaId) ? null : personaId.Trim().ToLowerInvariant(),
            Channel: channel,
            ContinuationToken: continuationToken,
            MaxTokens: resolvedMaxTokens,
            IncludeInsights: resolvedIncludeInsights,
            IncludeReasoning: resolvedIncludeReasoning,
            Languages: sanitizedLanguages);
    }
}
