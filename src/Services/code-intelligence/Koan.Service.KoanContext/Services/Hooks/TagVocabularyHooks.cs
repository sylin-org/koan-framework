using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Koan.Context.Models;
using Koan.Service.KoanContext.Infrastructure;
using Koan.Web.Hooks;
using Microsoft.Extensions.Caching.Memory;

namespace Koan.Context.Services.Hooks;

/// <summary>
/// Normalizes and maintains cache for tag vocabulary operations.
/// </summary>
public sealed class TagVocabularyHooks : IModelHook<TagVocabularyEntry>
{
    private readonly IMemoryCache _cache;

    public TagVocabularyHooks(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public int Order => 0;

    public Task OnBeforeFetchAsync(HookContext<TagVocabularyEntry> ctx, string id)
        => Task.CompletedTask;

    public Task OnAfterFetchAsync(HookContext<TagVocabularyEntry> ctx, TagVocabularyEntry? model)
        => Task.CompletedTask;

    public Task OnBeforeSaveAsync(HookContext<TagVocabularyEntry> ctx, TagVocabularyEntry model)
    {
        Normalize(model);
        return Task.CompletedTask;
    }

    public Task OnAfterSaveAsync(HookContext<TagVocabularyEntry> ctx, TagVocabularyEntry model)
    {
        Invalidate();
        return Task.CompletedTask;
    }

    public Task OnBeforeDeleteAsync(HookContext<TagVocabularyEntry> ctx, TagVocabularyEntry model)
        => Task.CompletedTask;

    public Task OnAfterDeleteAsync(HookContext<TagVocabularyEntry> ctx, TagVocabularyEntry model)
    {
        Invalidate();
        return Task.CompletedTask;
    }

    public Task OnBeforePatchAsync(HookContext<TagVocabularyEntry> ctx, string id, object patch)
        => Task.CompletedTask;

    public Task OnAfterPatchAsync(HookContext<TagVocabularyEntry> ctx, TagVocabularyEntry model)
    {
        Normalize(model);
        Invalidate();
        return Task.CompletedTask;
    }

    private static void Normalize(TagVocabularyEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Tag))
        {
            throw new ValidationException("Tag cannot be empty.");
        }

        entry.Tag = entry.Tag.Trim().ToLowerInvariant();
        entry.DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? null
            : entry.DisplayName.Trim();
        entry.Synonyms = TagEnvelope.NormalizeTags(entry.Synonyms).ToList();

        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            entry.Id = $"tag-vocab::{entry.Tag}";
        }
    }

    private void Invalidate() => _cache.Remove(Constants.CacheKeys.TagVocabulary);
}
