using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Koan.Context.Models;
using Koan.Service.KoanContext.Infrastructure;
using Koan.Web.Hooks;
using Microsoft.Extensions.Caching.Memory;

namespace Koan.Context.Services.Hooks;

/// <summary>
/// Normalizes tag rules and keeps downstream caches coherent.
/// </summary>
public sealed class TagRuleHooks : IModelHook<TagRule>
{
    private static readonly string[] AllowedScopes =
    [
        TagRuleScopes.File,
        TagRuleScopes.Frontmatter,
        TagRuleScopes.Chunk
    ];

    private static readonly string[] AllowedMatcherTypes =
    [
        TagRuleMatcherTypes.Path,
        TagRuleMatcherTypes.Extension,
        TagRuleMatcherTypes.Frontmatter,
        TagRuleMatcherTypes.ContentRegex,
        TagRuleMatcherTypes.Language
    ];

    private readonly IMemoryCache _cache;

    public TagRuleHooks(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public int Order => 0;

    public Task OnBeforeFetchAsync(HookContext<TagRule> ctx, string id) => Task.CompletedTask;

    public Task OnAfterFetchAsync(HookContext<TagRule> ctx, TagRule? model) => Task.CompletedTask;

    public Task OnBeforeSaveAsync(HookContext<TagRule> ctx, TagRule model)
    {
        Normalize(model);
        return Task.CompletedTask;
    }

    public async Task OnAfterSaveAsync(HookContext<TagRule> ctx, TagRule model)
    {
        await InvalidatePipelinesAsync(ctx).ConfigureAwait(false);
    }

    public Task OnBeforeDeleteAsync(HookContext<TagRule> ctx, TagRule model) => Task.CompletedTask;

    public async Task OnAfterDeleteAsync(HookContext<TagRule> ctx, TagRule model)
    {
        await InvalidatePipelinesAsync(ctx).ConfigureAwait(false);
    }

    public Task OnBeforePatchAsync(HookContext<TagRule> ctx, string id, object patch) => Task.CompletedTask;

    public async Task OnAfterPatchAsync(HookContext<TagRule> ctx, TagRule model)
    {
        Normalize(model);
        await InvalidatePipelinesAsync(ctx).ConfigureAwait(false);
    }

    private static void Normalize(TagRule model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            throw new ValidationException("Rule name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.Pattern))
        {
            throw new ValidationException("Rule pattern is required.");
        }

        model.Name = model.Name.Trim();
        model.Pattern = model.Pattern.Trim();

        model.Scope = ResolveAllowedValue(model.Scope, AllowedScopes, TagRuleScopes.File);
        model.MatcherType = ResolveAllowedValue(model.MatcherType, AllowedMatcherTypes, TagRuleMatcherTypes.Path);

        model.Tags = TagEnvelope.NormalizeTags(model.Tags).ToList();

        if (model.Tags.Count == 0)
        {
            throw new ValidationException("At least one tag must be emitted.");
        }

    model.Confidence = Math.Clamp(model.Confidence, 0f, 1f);
    model.Priority = Math.Clamp(model.Priority, 0, 10_000);

        if (string.IsNullOrWhiteSpace(model.Id))
        {
            var slug = ToSlug(model.Name);
            if (string.IsNullOrWhiteSpace(slug))
            {
                slug = Guid.NewGuid().ToString("n");
            }

            model.Id = $"tag-rule::{slug}";
        }
    }

    private async Task InvalidatePipelinesAsync(HookContext<TagRule> ctx)
    {
        try
        {
            var pipelines = await TagPipeline.All(ctx.Ct).ConfigureAwait(false);

            if (pipelines.Count == 0)
            {
                _cache.Remove(Constants.CacheKeys.TagPipeline("default"));
                return;
            }

            foreach (var pipeline in pipelines)
            {
                var key = Constants.CacheKeys.TagPipeline(NormalizePipelineName(pipeline.Name));
                _cache.Remove(key);
            }
        }
        catch (Exception ex)
        {
            ctx.Warn($"Failed to invalidate tag pipeline cache: {ex.Message}");
            _cache.Remove(Constants.CacheKeys.TagPipeline("default"));
        }
    }

    private static string NormalizePipelineName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? "default"
            : name.Trim().ToLowerInvariant();
    }

    private static string ResolveAllowedValue(string? value, IReadOnlyCollection<string> allowed, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return allowed.Any(option => string.Equals(option, normalized, StringComparison.OrdinalIgnoreCase))
            ? normalized
            : fallback;
    }

    private static string ToSlug(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        var collapsed = Regex.Replace(trimmed, "[^a-z0-9]+", "-");
        return collapsed.Trim('-');
    }
}