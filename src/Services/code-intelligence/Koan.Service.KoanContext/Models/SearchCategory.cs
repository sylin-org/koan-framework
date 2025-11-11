using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Context.Models;

/// <summary>
/// Declarative rule describing how tags should be inferred for a file or chunk.
/// </summary>
public class TagRule : Entity<TagRule>
{
    public string Name { get; set; } = string.Empty;
    public string Scope { get; set; } = TagRuleScopes.File;
    public string MatcherType { get; set; } = TagRuleMatcherTypes.Path;
    public string Pattern { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public float Confidence { get; set; } = 0.8f;
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;

    public static TagRule Create(
        string name,
        string scope,
        string matcherType,
        string pattern,
        IEnumerable<string> tags,
        float confidence = 0.8f,
        int priority = 100,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Rule name is required", nameof(name));

        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Pattern is required", nameof(pattern));

        var tagList = TagEnvelope.NormalizeTags(tags);
        if (tagList.Count == 0)
            throw new ArgumentException("At least one tag is required", nameof(tags));

        return new TagRule
        {
            Name = name,
            Scope = scope,
            MatcherType = matcherType,
            Pattern = pattern,
            Tags = tagList.ToList(),
            Confidence = confidence,
            Priority = priority,
            IsActive = isActive
        };
    }
}

/// <summary>
/// Declarative pipeline describing which tag rules execute and tag limits.
/// </summary>
public class TagPipeline : Entity<TagPipeline>
{
    public string Name { get; set; } = "default";
    public string Description { get; set; } = string.Empty;
    public List<string> RuleIds { get; set; } = new();
    public int MaxPrimaryTags { get; set; } = 6;
    public int MaxSecondaryTags { get; set; } = 10;
    public bool EnableAiFallback { get; set; } = false;

    public static TagPipeline Create(
        string name,
        IEnumerable<string> ruleIds,
        string? description = null,
        int maxPrimary = 6,
        int maxSecondary = 10,
        bool enableAiFallback = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pipeline name is required", nameof(name));

        var rules = ruleIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? new();

        return new TagPipeline
        {
            Name = name,
            Description = description ?? string.Empty,
            RuleIds = rules,
            MaxPrimaryTags = maxPrimary,
            MaxSecondaryTags = maxSecondary,
            EnableAiFallback = enableAiFallback
        };
    }
}

/// <summary>
/// Canonical vocabulary entry with synonyms and metadata.
/// </summary>
public class TagVocabularyEntry : Entity<TagVocabularyEntry>
{
    public string Tag { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public List<string> Synonyms { get; set; } = new();
    public bool IsPrimary { get; set; } = true;

    public static TagVocabularyEntry Create(
        string tag,
        IEnumerable<string>? synonyms = null,
        string? displayName = null,
        bool isPrimary = true)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException("Tag cannot be empty", nameof(tag));

        return new TagVocabularyEntry
        {
            Tag = tag.Trim().ToLowerInvariant(),
            DisplayName = displayName,
            Synonyms = TagEnvelope.NormalizeTags(synonyms).ToList(),
            IsPrimary = isPrimary
        };
    }
}

/// <summary>
/// Audit entry persisted on a chunk describing which rule emitted a tag.
/// </summary>
public record TagAuditEntry(
    string Tag,
    string RuleId,
    string Scope,
    float Confidence,
    string Source);

/// <summary>
/// Aggregated tag payload used by entities to store tag state.
/// </summary>
public record TagEnvelope(
    IReadOnlyList<string> Primary,
    IReadOnlyList<string> Secondary,
    IReadOnlyList<string> File,
    IReadOnlyDictionary<string, string> Frontmatter,
    IReadOnlyList<TagAuditEntry> Audit)
{
    public static TagEnvelope Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<TagAuditEntry>());

    public TagEnvelope Normalize()
    {
        return new TagEnvelope(
            NormalizeTags(Primary),
            NormalizeTags(Secondary),
            NormalizeTags(File),
            NormalizeDictionary(Frontmatter),
            Audit?.ToArray() ?? Array.Empty<TagAuditEntry>());
    }

    public TagEnvelope WithPrimary(IEnumerable<string>? tags) => this with { Primary = NormalizeTags(tags) };

    public TagEnvelope WithSecondary(IEnumerable<string>? tags) => this with { Secondary = NormalizeTags(tags) };

    public TagEnvelope WithFile(IEnumerable<string>? tags) => this with { File = NormalizeTags(tags) };

    public TagEnvelope WithFrontmatter(IReadOnlyDictionary<string, string>? metadata)
        => this with { Frontmatter = NormalizeDictionary(metadata) };

    public TagEnvelope WithAudit(IEnumerable<TagAuditEntry>? auditEntries)
        => this with { Audit = auditEntries?.ToArray() ?? Array.Empty<TagAuditEntry>() };

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags == null)
        {
            return Array.Empty<string>();
        }

        return tags
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> NormalizeDictionary(IReadOnlyDictionary<string, string>? source)
    {
        if (source == null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Lookup helpers for tag rule metadata.
/// </summary>
public static class TagRuleScopes
{
    public const string File = "file";
    public const string Frontmatter = "frontmatter";
    public const string Chunk = "chunk";
}

public static class TagRuleMatcherTypes
{
    public const string Path = "path";
    public const string Extension = "extension";
    public const string Frontmatter = "frontmatter";
    public const string ContentRegex = "contentRegex";
    public const string Language = "language";
}

public static class TagJson
{
}
