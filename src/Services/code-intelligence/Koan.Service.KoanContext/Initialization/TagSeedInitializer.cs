using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Context.Models;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Service.KoanContext.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Initialization;

/// <summary>
/// Seeds baseline tag vocabulary, rules, pipelines, and personas at startup to ensure
/// deterministic defaults for indexing and search.
/// </summary>
public sealed class TagSeedInitializer : IHostedService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TagSeedInitializer> _logger;
    private TagSeedSummary? _lastSummary;
    private bool _completed;

    public TagSeedInitializer(IServiceProvider serviceProvider, ILogger<TagSeedInitializer> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(force: false, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<TagSeedSummary> EnsureSeededAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force && _completed && _lastSummary is not null)
        {
            _logger.LogDebug("Tag seeding skipped (already completed)");
            return _lastSummary;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && _completed && _lastSummary is not null)
            {
                return _lastSummary;
            }

            if (AppHost.Current is null)
            {
                AppHost.Current = _serviceProvider;
                _logger.LogDebug("AppHost.Current initialized for tag seeding");
            }

            using var scope = _serviceProvider.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            using var partitionScope = EntityContext.With(partition: null);

            var reports = new List<TagSeedReport>(4)
            {
                await SeedVocabularyAsync(cancellationToken).ConfigureAwait(false),
                await SeedRulesAsync(cancellationToken).ConfigureAwait(false),
                await SeedPipelinesAsync(cancellationToken).ConfigureAwait(false),
                await SeedPersonasAsync(cancellationToken).ConfigureAwait(false)
            };

            cache.Remove(Constants.CacheKeys.TagVocabulary);
            foreach (var pipeline in PipelineSeeds)
            {
                cache.Remove(Constants.CacheKeys.TagPipeline(pipeline.Name));
            }

            _completed = true;

            var summaryText = string.Join(", ", reports.Select(static report => $"{report.Segment}: +{report.Created}/~{report.Updated}"));
            _logger.LogInformation(
                "Tag seed run completed (forced: {Forced}) – {Summary}",
                force,
                summaryText);

            var summary = new TagSeedSummary(
                Completed: true,
                Forced: force,
                Timestamp: DateTimeOffset.UtcNow,
                Reports: reports);

            _lastSummary = summary;
            return summary;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static readonly ImmutableArray<TagVocabularySeed> VocabularySeeds =
    [
        new TagVocabularySeed("docs", "Documentation", new[] { "documentation", "doc" }),
        new TagVocabularySeed("adr", "Architecture Decision", new[] { "decision", "adr" }),
        new TagVocabularySeed("api", "API", new[] { "apis", "endpoint" }),
        new TagVocabularySeed("guide", "Guide", new[] { "howto", "how-to" }),
        new TagVocabularySeed("sample", "Sample", new[] { "example", "snippet" })
    ];

    private static async Task<TagSeedReport> SeedVocabularyAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        var updated = 0;

        foreach (var seed in VocabularySeeds)
        {
            var existing = await TagVocabularyEntry.Query(e => e.Tag == seed.Tag, cancellationToken).ConfigureAwait(false);
            var normalizedSynonyms = TagEnvelope.NormalizeTags(seed.Synonyms).ToArray();

            if (existing.Count == 0)
            {
                var entry = TagVocabularyEntry.Create(seed.Tag, normalizedSynonyms, seed.DisplayName, true);
                entry.Id = seed.Id;
                await entry.Save(cancellationToken).ConfigureAwait(false);
                created++;
                continue;
            }

            var current = existing[0];
            current.DisplayName = seed.DisplayName;
            current.IsPrimary = true;
            current.Synonyms = normalizedSynonyms.ToList();
            await current.Save(cancellationToken).ConfigureAwait(false);
            updated++;
        }

        return new TagSeedReport("vocabulary", created, updated);
    }

    private static readonly ImmutableArray<TagRuleSeed> RuleSeeds =
    [
        new TagRuleSeed(
            Id: "tag-rule::docs-path",
            Name: "Docs Path",
            Scope: TagRuleScopes.File,
            MatcherType: TagRuleMatcherTypes.Path,
            Pattern: "docs/**",
            Tags: new[] { "docs" },
            Confidence: 0.9f,
            Priority: 100),
        new TagRuleSeed(
            Id: "tag-rule::decisions",
            Name: "Decisions",
            Scope: TagRuleScopes.File,
            MatcherType: TagRuleMatcherTypes.Path,
            Pattern: "docs/decisions/**",
            Tags: new[] { "adr" },
            Confidence: 0.95f,
            Priority: 110),
        new TagRuleSeed(
            Id: "tag-rule::api",
            Name: "API Surface",
            Scope: TagRuleScopes.File,
            MatcherType: TagRuleMatcherTypes.Path,
            Pattern: "docs/api/**",
            Tags: new[] { "api" },
            Confidence: 0.85f,
            Priority: 90),
        new TagRuleSeed(
            Id: "tag-rule::guides",
            Name: "Guides",
            Scope: TagRuleScopes.File,
            MatcherType: TagRuleMatcherTypes.Path,
            Pattern: "docs/guides/**",
            Tags: new[] { "guide" },
            Confidence: 0.8f,
            Priority: 80),
        new TagRuleSeed(
            Id: "tag-rule::samples",
            Name: "Samples",
            Scope: TagRuleScopes.File,
            MatcherType: TagRuleMatcherTypes.Path,
            Pattern: "samples/**",
            Tags: new[] { "sample" },
            Confidence: 0.75f,
            Priority: 70)
    ];

    private static async Task<TagSeedReport> SeedRulesAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        var updated = 0;

        foreach (var seed in RuleSeeds)
        {
            var existing = await TagRule.Query(r => r.Id == seed.Id, cancellationToken).ConfigureAwait(false);

            if (existing.Count == 0)
            {
                var rule = TagRule.Create(seed.Name, seed.Scope, seed.MatcherType, seed.Pattern, seed.Tags, seed.Confidence, seed.Priority);
                rule.Id = seed.Id;
                await rule.Save(cancellationToken).ConfigureAwait(false);
                created++;
                continue;
            }

            var current = existing[0];
            current.Name = seed.Name;
            current.Scope = seed.Scope;
            current.MatcherType = seed.MatcherType;
            current.Pattern = seed.Pattern;
            current.Tags = TagEnvelope.NormalizeTags(seed.Tags).ToList();
            current.Confidence = seed.Confidence;
            current.Priority = seed.Priority;
            current.IsActive = true;
            await current.Save(cancellationToken).ConfigureAwait(false);
            updated++;
        }

        return new TagSeedReport("rules", created, updated);
    }

    private static readonly ImmutableArray<TagPipelineSeed> PipelineSeeds =
    [
        new TagPipelineSeed(
            Id: "tag-pipeline::default",
            Name: "default",
            Description: "Default Koan context tag pipeline",
            RuleIds: RuleSeeds.Select(r => r.Id).ToArray(),
            MaxPrimaryTags: 6,
            MaxSecondaryTags: 10,
            EnableAiFallback: false)
    ];

    private static async Task<TagSeedReport> SeedPipelinesAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        var updated = 0;

        foreach (var seed in PipelineSeeds)
        {
            var existing = await TagPipeline.Query(p => p.Name == seed.Name, cancellationToken).ConfigureAwait(false);

            if (existing.Count == 0)
            {
                var pipeline = TagPipeline.Create(seed.Name, seed.RuleIds, seed.Description, seed.MaxPrimaryTags, seed.MaxSecondaryTags, seed.EnableAiFallback);
                pipeline.Id = seed.Id;
                await pipeline.Save(cancellationToken).ConfigureAwait(false);
                created++;
                continue;
            }

            var current = existing[0];
            current.Description = seed.Description;
            current.RuleIds = seed.RuleIds.ToList();
            current.MaxPrimaryTags = seed.MaxPrimaryTags;
            current.MaxSecondaryTags = seed.MaxSecondaryTags;
            current.EnableAiFallback = seed.EnableAiFallback;
            await current.Save(cancellationToken).ConfigureAwait(false);
            updated++;
        }

        return new TagSeedReport("pipelines", created, updated);
    }

    private static readonly ImmutableArray<SearchPersonaSeed> PersonaSeeds =
    [
        new SearchPersonaSeed(
            Id: "persona::general",
            Name: "general",
            DisplayName: "General",
            Description: "Balanced retrieval across docs and API surfaces",
            SemanticWeight: 0.6f,
            TagWeight: 0.3f,
            RecencyWeight: 0.1f,
            MaxTokens: 6000,
            IncludeInsights: true,
            IncludeReasoning: true,
            TagBoosts: new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["guide"] = 0.2f,
                ["docs"] = 0.1f
            }),
        new SearchPersonaSeed(
            Id: "persona::api-first",
            Name: "api-first",
            DisplayName: "API First",
            Description: "Boost endpoints and technical reference material",
            SemanticWeight: 0.55f,
            TagWeight: 0.35f,
            RecencyWeight: 0.1f,
            MaxTokens: 5000,
            IncludeInsights: false,
            IncludeReasoning: true,
            TagBoosts: new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["api"] = 0.4f,
                ["sample"] = 0.15f
            },
            DefaultAny: new[] { "api" }),
        new SearchPersonaSeed(
            Id: "persona::architecture",
            Name: "architecture",
            DisplayName: "Architecture",
            Description: "Highlight architectural decisions and governance",
            SemanticWeight: 0.5f,
            TagWeight: 0.4f,
            RecencyWeight: 0.1f,
            MaxTokens: 7000,
            IncludeInsights: true,
            IncludeReasoning: true,
            TagBoosts: new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["adr"] = 0.5f,
                ["docs"] = 0.15f
            },
            DefaultAll: new[] { "adr" })
    ];

    private static async Task<TagSeedReport> SeedPersonasAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        var updated = 0;

        foreach (var seed in PersonaSeeds)
        {
            var existing = await SearchPersona.Query(p => p.Name == seed.Name, cancellationToken).ConfigureAwait(false);

            if (existing.Count == 0)
            {
                var personaBoosts = seed.TagBoosts
                    .ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

                var persona = SearchPersona.Create(
                    name: seed.Name,
                    displayName: seed.DisplayName,
                    description: seed.Description,
                    semanticWeight: seed.SemanticWeight,
                    tagWeight: seed.TagWeight,
                    recencyWeight: seed.RecencyWeight,
                    maxTokens: seed.MaxTokens,
                    tagBoosts: personaBoosts,
                    defaultAny: seed.DefaultTagsAny,
                    defaultAll: seed.DefaultTagsAll,
                    defaultExclude: seed.DefaultTagsExclude,
                    includeInsights: seed.IncludeInsights,
                    includeReasoning: seed.IncludeReasoning);
                persona.Id = seed.Id;
                await persona.Save(cancellationToken).ConfigureAwait(false);
                created++;
                continue;
            }

            var current = existing[0];
            current.DisplayName = seed.DisplayName;
            current.Description = seed.Description;
            current.SemanticWeight = seed.SemanticWeight;
            current.TagWeight = seed.TagWeight;
            current.RecencyWeight = seed.RecencyWeight;
            current.MaxTokens = seed.MaxTokens;
            current.IncludeInsights = seed.IncludeInsights;
            current.IncludeReasoning = seed.IncludeReasoning;
            current.TagBoosts = seed.TagBoosts
                .ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            current.DefaultTagsAny = TagEnvelope.NormalizeTags(seed.DefaultTagsAny).ToList();
            current.DefaultTagsAll = TagEnvelope.NormalizeTags(seed.DefaultTagsAll).ToList();
            current.DefaultTagsExclude = TagEnvelope.NormalizeTags(seed.DefaultTagsExclude).ToList();
            current.IsActive = true;
            await current.Save(cancellationToken).ConfigureAwait(false);
            updated++;
        }

        return new TagSeedReport("personas", created, updated);
    }

    private sealed record TagVocabularySeed(string Tag, string DisplayName, IEnumerable<string> Synonyms)
    {
        public string Id { get; } = $"tag-vocab::{Tag}";
    }

    private sealed record TagRuleSeed(
        string Id,
        string Name,
        string Scope,
        string MatcherType,
        string Pattern,
        IEnumerable<string> Tags,
        float Confidence,
        int Priority);

    private sealed record TagPipelineSeed(
        string Id,
        string Name,
        string Description,
        IReadOnlyList<string> RuleIds,
        int MaxPrimaryTags,
        int MaxSecondaryTags,
        bool EnableAiFallback);

    private sealed record SearchPersonaSeed(
        string Id,
        string Name,
        string DisplayName,
        string Description,
        float SemanticWeight,
        float TagWeight,
        float RecencyWeight,
        int MaxTokens,
        bool IncludeInsights,
        bool IncludeReasoning,
        IReadOnlyDictionary<string, float> TagBoosts,
        IEnumerable<string>? DefaultAny = null,
        IEnumerable<string>? DefaultAll = null,
        IEnumerable<string>? DefaultExclude = null)
    {
        public IReadOnlyList<string> DefaultTagsAny { get; } = DefaultAny?.ToArray() ?? Array.Empty<string>();
        public IReadOnlyList<string> DefaultTagsAll { get; } = DefaultAll?.ToArray() ?? Array.Empty<string>();
        public IReadOnlyList<string> DefaultTagsExclude { get; } = DefaultExclude?.ToArray() ?? Array.Empty<string>();
    }

    public sealed record TagSeedSummary(bool Completed, bool Forced, DateTimeOffset Timestamp, IReadOnlyList<TagSeedReport> Reports);

    public sealed record TagSeedReport(string Segment, int Created, int Updated);
}
