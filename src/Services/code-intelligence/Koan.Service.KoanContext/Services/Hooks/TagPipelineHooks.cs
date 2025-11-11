using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Koan.Context.Models;
using Koan.Service.KoanContext.Infrastructure;
using Koan.Web.Hooks;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.Tasks;

namespace Koan.Context.Services.Hooks;

/// <summary>
/// Normalizes tag pipelines and purges related caches when they change.
/// </summary>
public sealed class TagPipelineHooks : IModelHook<TagPipeline>
{
    private readonly IMemoryCache _cache;

    public TagPipelineHooks(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public int Order => 0;

    public Task OnBeforeFetchAsync(HookContext<TagPipeline> ctx, string id) => Task.CompletedTask;

    public Task OnAfterFetchAsync(HookContext<TagPipeline> ctx, TagPipeline? model) => Task.CompletedTask;

    public Task OnBeforeSaveAsync(HookContext<TagPipeline> ctx, TagPipeline model)
    {
        Normalize(model);
        return Task.CompletedTask;
    }

    public Task OnBeforeDeleteAsync(HookContext<TagPipeline> ctx, TagPipeline model) => Task.CompletedTask;

    public async Task OnAfterSaveAsync(HookContext<TagPipeline> ctx, TagPipeline model)
    {
        Invalidate(model);
        await InvalidateLinkedRulesAsync(ctx, model.RuleIds).ConfigureAwait(false);
    }

    public async Task OnAfterDeleteAsync(HookContext<TagPipeline> ctx, TagPipeline model)
    {
        Invalidate(model);
        await InvalidateLinkedRulesAsync(ctx, model.RuleIds).ConfigureAwait(false);
    }

    public Task OnBeforePatchAsync(HookContext<TagPipeline> ctx, string id, object patch) => Task.CompletedTask;

    public async Task OnAfterPatchAsync(HookContext<TagPipeline> ctx, TagPipeline model)
    {
        Normalize(model);
        Invalidate(model);
        await InvalidateLinkedRulesAsync(ctx, model.RuleIds).ConfigureAwait(false);
    }

    private static void Normalize(TagPipeline pipeline)
    {
        if (string.IsNullOrWhiteSpace(pipeline.Name))
        {
            throw new ValidationException("Pipeline name is required.");
        }

        pipeline.Name = pipeline.Name.Trim().ToLowerInvariant();
        pipeline.Description = string.IsNullOrWhiteSpace(pipeline.Description)
            ? string.Empty
            : pipeline.Description.Trim();

        pipeline.RuleIds = (pipeline.RuleIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        pipeline.MaxPrimaryTags = Math.Clamp(pipeline.MaxPrimaryTags, 1, 50);
        pipeline.MaxSecondaryTags = Math.Clamp(pipeline.MaxSecondaryTags, 0, 200);

        if (string.IsNullOrWhiteSpace(pipeline.Id))
        {
            pipeline.Id = $"tag-pipeline::{pipeline.Name}";
        }
    }

    private void Invalidate(TagPipeline pipeline)
    {
        var cacheKey = Constants.CacheKeys.TagPipeline(pipeline.Name);
        _cache.Remove(cacheKey);
    }

    private async Task InvalidateLinkedRulesAsync(HookContext<TagPipeline> ctx, IReadOnlyCollection<string>? ruleIds)
    {
        if (ruleIds == null || ruleIds.Count == 0)
        {
            return;
        }

        try
        {
            var pipelines = await TagPipeline.All(ctx.Ct).ConfigureAwait(false);
            foreach (var candidate in pipelines)
            {
                var normalizedName = string.IsNullOrWhiteSpace(candidate.Name)
                    ? "default"
                    : candidate.Name.Trim().ToLowerInvariant();
                _cache.Remove(Constants.CacheKeys.TagPipeline(normalizedName));
            }
        }
        catch (Exception ex)
        {
            ctx.Warn($"Failed to invalidate tag pipeline caches: {ex.Message}");
            _cache.Remove(Constants.CacheKeys.TagPipeline("default"));
        }
    }
}