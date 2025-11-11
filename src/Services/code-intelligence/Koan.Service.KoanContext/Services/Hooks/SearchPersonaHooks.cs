using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Koan.Context.Models;
using Koan.Service.KoanContext.Infrastructure;
using Koan.Web.Hooks;
using Microsoft.Extensions.Caching.Memory;

namespace Koan.Context.Services.Hooks;

/// <summary>
/// Normalizes search personas and invalidates persona caches.
/// </summary>
public sealed class SearchPersonaHooks : IModelHook<SearchPersona>
{
    private readonly IMemoryCache _cache;

    public SearchPersonaHooks(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public int Order => 0;

    public Task OnBeforeFetchAsync(HookContext<SearchPersona> ctx, string id) => Task.CompletedTask;

    public Task OnAfterFetchAsync(HookContext<SearchPersona> ctx, SearchPersona? model) => Task.CompletedTask;

    public Task OnBeforeSaveAsync(HookContext<SearchPersona> ctx, SearchPersona model)
    {
        Normalize(model);
        return Task.CompletedTask;
    }

    public Task OnBeforeDeleteAsync(HookContext<SearchPersona> ctx, SearchPersona model) => Task.CompletedTask;

    public Task OnBeforePatchAsync(HookContext<SearchPersona> ctx, string id, object patch) => Task.CompletedTask;

    public Task OnAfterPatchAsync(HookContext<SearchPersona> ctx, SearchPersona model)
    {
        Normalize(model);
        Invalidate(model.Name);
        return Task.CompletedTask;
    }

    public Task OnAfterSaveAsync(HookContext<SearchPersona> ctx, SearchPersona model)
    {
        Invalidate(model.Name);
        return Task.CompletedTask;
    }

    public Task OnAfterDeleteAsync(HookContext<SearchPersona> ctx, SearchPersona model)
    {
        Invalidate(model.Name);
        return Task.CompletedTask;
    }

    private static void Normalize(SearchPersona persona)
    {
        if (string.IsNullOrWhiteSpace(persona.Name))
        {
            throw new ValidationException("Persona name is required.");
        }

        persona.Name = persona.Name.Trim().ToLowerInvariant();
        persona.DisplayName = string.IsNullOrWhiteSpace(persona.DisplayName)
            ? persona.Name
            : persona.DisplayName.Trim();
        persona.Description = string.IsNullOrWhiteSpace(persona.Description)
            ? string.Empty
            : persona.Description.Trim();

        persona.SemanticWeight = Math.Clamp(persona.SemanticWeight, 0f, 1f);
        persona.TagWeight = Math.Clamp(persona.TagWeight, 0f, 1f);
        persona.RecencyWeight = Math.Clamp(persona.RecencyWeight, 0f, 1f);
        persona.MaxTokens = Math.Clamp(persona.MaxTokens, 1000, 20000);

        persona.TagBoosts = NormalizeBoosts(persona.TagBoosts);
        persona.DefaultTagsAny = TagEnvelope.NormalizeTags(persona.DefaultTagsAny).ToList();
        persona.DefaultTagsAll = TagEnvelope.NormalizeTags(persona.DefaultTagsAll).ToList();
        persona.DefaultTagsExclude = TagEnvelope.NormalizeTags(persona.DefaultTagsExclude).ToList();

        if (string.IsNullOrWhiteSpace(persona.Id))
        {
            persona.Id = $"persona::{persona.Name}";
        }
    }

    private static Dictionary<string, float> NormalizeBoosts(IDictionary<string, float>? boosts)
    {
        if (boosts is null || boosts.Count == 0)
        {
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in boosts)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                continue;
            }

            var key = kvp.Key.Trim().ToLowerInvariant();
            normalized[key] = Math.Clamp(kvp.Value, 0f, 1f);
        }

        return normalized;
    }

    private void Invalidate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var key = Constants.CacheKeys.Persona(name.Trim().ToLowerInvariant());
        _cache.Remove(key);
    }
}