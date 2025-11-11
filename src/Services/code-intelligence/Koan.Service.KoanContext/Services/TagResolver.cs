using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Koan.Context.Models;
using Koan.Data.Core;
using Koan.Service.KoanContext.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

/// <summary>
/// Resolves tag envelopes for files and chunks by executing configured tag pipelines.
/// </summary>
public class TagResolver
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<TagResolver> _logger;

    public TagResolver(IMemoryCache cache, ILogger<TagResolver> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TagResolutionResult> ResolveAsync(TagResolverInput input, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPipelineSnapshotAsync(input.PipelineName, cancellationToken);
        var vocabulary = await GetVocabularySnapshotAsync(cancellationToken);

        var emitted = new Dictionary<string, ResolvedTag>(StringComparer.Ordinal);
        var audit = new List<TagAuditEntry>();

        foreach (var rule in snapshot.Rules)
        {
            if (!rule.IsActive)
            {
                continue;
            }

            if (!IsRuleInScope(rule.Scope, input.Stage))
            {
                continue;
            }

            if (!Matches(rule, input))
            {
                continue;
            }

            foreach (var rawTag in rule.Tags)
            {
                var canonical = vocabulary.GetCanonical(rawTag);
                var current = emitted.GetValueOrDefault(canonical);
                if (current == null || rule.Confidence >= current.Confidence)
                {
                    emitted[canonical] = new ResolvedTag(
                        Tag: canonical,
                        Confidence: rule.Confidence,
                        Scope: rule.Scope,
                        RuleId: rule.Id ?? "");
                }

                audit.Add(new TagAuditEntry(
                    Tag: canonical,
                    RuleId: rule.Id ?? "",
                    Scope: rule.Scope,
                    Confidence: rule.Confidence,
                    Source: rule.MatcherType));
            }
        }

        var ordered = emitted.Values
            .OrderByDescending(static tag => tag.Confidence)
            .ThenBy(static tag => tag.Tag, StringComparer.Ordinal)
            .ToList();

        var primary = ordered
            .Take(snapshot.Pipeline.MaxPrimaryTags)
            .Select(static tag => tag.Tag)
            .ToArray();

        var secondary = ordered
            .Skip(primary.Length)
            .Take(snapshot.Pipeline.MaxSecondaryTags)
            .Select(static tag => tag.Tag)
            .ToArray();

        var normalizedFileTags = TagEnvelope.NormalizeTags(input.FileTags);
        var normalizedFrontmatter = NormalizeFrontmatter(input.Frontmatter);

        return new TagResolutionResult(
            Envelope: new TagEnvelope(primary, secondary, normalizedFileTags, normalizedFrontmatter, audit),
            Primary: primary,
            Secondary: secondary,
            AppliedRules: ordered.Select(static tag => tag.RuleId).ToArray());
    }

    private static bool IsRuleInScope(string scope, TagResolverStage stage)
    {
        return stage switch
        {
            TagResolverStage.File => scope is TagRuleScopes.File or TagRuleScopes.Frontmatter,
            TagResolverStage.Chunk => scope is TagRuleScopes.File or TagRuleScopes.Frontmatter or TagRuleScopes.Chunk,
            _ => false
        };
    }

    private bool Matches(TagRule rule, TagResolverInput input)
    {
        return rule.MatcherType switch
        {
            TagRuleMatcherTypes.Path => MatchesPath(rule.Pattern, input.RelativePath),
            TagRuleMatcherTypes.Extension => MatchesExtension(rule.Pattern, input.RelativePath),
            TagRuleMatcherTypes.Frontmatter => MatchesFrontmatter(rule.Pattern, input.Frontmatter),
            TagRuleMatcherTypes.ContentRegex => MatchesContent(rule.Pattern, input.ChunkText),
            TagRuleMatcherTypes.Language => MatchesLanguage(rule.Pattern, input.Language),
            _ => false
        };
    }

    private static bool MatchesPath(string pattern, string path)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPattern = pattern.Replace('\\', '/').ToLowerInvariant();
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();

        if (normalizedPattern.Contains("**"))
        {
            var segments = normalizedPattern.Split("**", StringSplitOptions.RemoveEmptyEntries);
            var prefix = segments.Length > 0 ? segments[0] : string.Empty;
            var suffix = segments.Length > 1 ? segments[1] : string.Empty;
            var matchesPrefix = string.IsNullOrEmpty(prefix) || normalizedPath.StartsWith(prefix, StringComparison.Ordinal);
            var matchesSuffix = string.IsNullOrEmpty(suffix) || normalizedPath.EndsWith(suffix, StringComparison.Ordinal);
            return matchesPrefix && matchesSuffix;
        }

        if (normalizedPattern.Contains('*'))
        {
            var escaped = Regex.Escape(normalizedPattern).Replace("\\*", ".*");
            return Regex.IsMatch(normalizedPath, $"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return normalizedPath.Equals(normalizedPattern, StringComparison.Ordinal);
    }

    private static bool MatchesExtension(string pattern, string path)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(path);
        return string.Equals(extension, pattern.StartsWith('.') ? pattern : $".{pattern}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesFrontmatter(string pattern, IReadOnlyDictionary<string, string> frontmatter)
    {
        if (string.IsNullOrWhiteSpace(pattern) || frontmatter.Count == 0)
        {
            return false;
        }

        var parts = pattern.Split('=', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        var key = parts[0];
        var value = parts[1];
        return frontmatter.TryGetValue(key, out var existing) &&
               string.Equals(existing, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesContent(string pattern, string? content)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool MatchesLanguage(string pattern, string? language)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return string.Equals(pattern, language, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TagPipelineSnapshot> GetPipelineSnapshotAsync(string? pipelineName, CancellationToken cancellationToken)
    {
        using var partitionScope = EntityContext.With(partition: null);
        var targetPipelineName = string.IsNullOrWhiteSpace(pipelineName)
            ? "default"
            : pipelineName.Trim().ToLowerInvariant();
        var cacheKey = Constants.CacheKeys.TagPipeline(targetPipelineName);

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var pipelineQuery = await TagPipeline.Query(p => p.Name == targetPipelineName, cancellationToken);
            var pipeline = pipelineQuery.FirstOrDefault() ?? new TagPipeline { Name = targetPipelineName, RuleIds = new List<string>() };

            var ruleIds = pipeline.RuleIds ?? new List<string>();
            IReadOnlyList<TagRule> rules;

            if (ruleIds.Count == 0)
            {
                rules = Array.Empty<TagRule>();
            }
            else
            {
                var result = await TagRule.Query(r => ruleIds.Contains(r.Id!), cancellationToken);
                rules = result.OrderByDescending(r => r.Priority).ThenBy(r => r.Name).ToArray();
            }

            _logger.LogDebug("Loaded tag pipeline {Pipeline} with {RuleCount} rules", targetPipelineName, rules.Count);

            return new TagPipelineSnapshot(pipeline, rules);
    }) ?? new TagPipelineSnapshot(new TagPipeline { Name = targetPipelineName, RuleIds = new List<string>() }, Array.Empty<TagRule>());
    }

    private async Task<TagVocabularySnapshot> GetVocabularySnapshotAsync(CancellationToken cancellationToken)
    {
    using var partitionScope = EntityContext.With(partition: null);
    const string cacheKey = Constants.CacheKeys.TagVocabulary;

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            var entries = await TagVocabularyEntry.All(cancellationToken);
            var synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entryItem in entries)
            {
                var canonical = entryItem.Tag.Trim().ToLowerInvariant();
                synonyms[canonical] = canonical;

                foreach (var synonym in entryItem.Synonyms)
                {
                    var normalized = synonym.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        synonyms[normalized] = canonical;
                    }
                }
            }

            return new TagVocabularySnapshot(synonyms.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
        }) ?? new TagVocabularySnapshot(FrozenDictionary<string, string>.Empty);
    }

    private static IReadOnlyDictionary<string, string> NormalizeFrontmatter(IReadOnlyDictionary<string, string>? frontmatter)
    {
        if (frontmatter == null || frontmatter.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(frontmatter, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ResolvedTag(string Tag, float Confidence, string Scope, string RuleId);

    private sealed record TagPipelineSnapshot(TagPipeline Pipeline, IReadOnlyList<TagRule> Rules);

    private sealed record TagVocabularySnapshot(FrozenDictionary<string, string> Lookup)
    {
        public string GetCanonical(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return Lookup.TryGetValue(normalized, out var canonical)
                ? canonical
                : normalized;
        }
    }
}

public enum TagResolverStage
{
    File,
    Chunk
}

public sealed record TagResolverInput(
    string ProjectId,
    string RelativePath,
    TagResolverStage Stage,
    string? PipelineName,
    string? Language,
    string? ChunkText,
    IReadOnlyDictionary<string, string> Frontmatter,
    IReadOnlyList<string> FileTags)
{
    public static TagResolverInput ForFile(
        string projectId,
        string relativePath,
        string? pipelineName,
        string? language,
        IReadOnlyDictionary<string, string>? frontmatter,
        IEnumerable<string>? fileTags = null)
    {
        return new TagResolverInput(
            ProjectId: projectId,
            RelativePath: relativePath,
            Stage: TagResolverStage.File,
            PipelineName: pipelineName,
            Language: language,
            ChunkText: null,
            Frontmatter: NormalizeFrontmatter(frontmatter),
            FileTags: TagEnvelope.NormalizeTags(fileTags));
    }

    public TagResolverInput ForChunk(string? language, string chunkText, IReadOnlyList<string> inheritedTags)
    {
        return new TagResolverInput(
            ProjectId,
            RelativePath,
            TagResolverStage.Chunk,
            PipelineName,
            language,
            chunkText,
            Frontmatter,
            TagEnvelope.NormalizeTags(inheritedTags));
    }

    private static IReadOnlyDictionary<string, string> NormalizeFrontmatter(IReadOnlyDictionary<string, string>? source)
    {
        if (source == null || source.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record TagResolutionResult(
    TagEnvelope Envelope,
    IReadOnlyList<string> Primary,
    IReadOnlyList<string> Secondary,
    IReadOnlyList<string> AppliedRules);
