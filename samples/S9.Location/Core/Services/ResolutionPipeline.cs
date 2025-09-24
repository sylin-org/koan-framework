using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S9.Location.Core.Models;
using S9.Location.Core.Options;

namespace S9.Location.Core.Services;

public sealed record ResolutionOutcome(string CanonicalLocationId, bool CacheHit, double Confidence);

public interface IResolutionPipeline
{
    Task<ResolutionOutcome> HarmonizeAsync(RawLocation location, CancellationToken ct = default);
}

public sealed class ResolutionPipeline : IResolutionPipeline
{
    private readonly INormalizationService _normalizationService;
    private readonly LocationOptions _options;
    private readonly ILogger<ResolutionPipeline> _logger;

    public ResolutionPipeline(
        INormalizationService normalizationService,
        IOptions<LocationOptions> options,
        ILogger<ResolutionPipeline> logger)
    {
        _normalizationService = normalizationService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ResolutionOutcome> HarmonizeAsync(RawLocation location, CancellationToken ct = default)
    {
        if (location == null) throw new ArgumentNullException(nameof(location));

        var normalization = _normalizationService.Normalize(location.SourceSystem, location.Address);
        Dictionary<string, object?>? aiAssistMetadata = null;

        if (_options.AiAssist.Enabled && normalization.Confidence < _options.AiAssist.ConfidenceThreshold)
        {
            var aiResult = await TryAiAssistAsync(location.SourceSystem, location.Address, normalization, ct);
            if (aiResult is not null)
            {
                normalization = aiResult.Value.Normalization;
                aiAssistMetadata = new Dictionary<string, object?>
                {
                    ["used"] = true,
                    ["model"] = aiResult.Value.Model,
                    ["suggestion"] = aiResult.Value.CorrectedAddress,
                    ["confidence"] = aiResult.Value.Normalization.Confidence
                };
            }
        }

        location.Metadata ??= new Dictionary<string, object?>();
        location.NormalizedAddress = normalization.Normalized;
        location.Metadata["normalized_address"] = normalization.Normalized;
        location.AddressHash = normalization.Hash;
        location.Metadata["hash"] = normalization.Hash;
        location.Metadata["confidence"] = normalization.Confidence;
        foreach (var token in normalization.Tokens)
        {
            location.Metadata[$"token:{token.Key}"] = token.Value;
        }
        if (aiAssistMetadata != null)
        {
            location.Metadata["ai_assist"] = aiAssistMetadata;
        }

        if (_options.Cache.Enabled && !string.IsNullOrEmpty(normalization.Hash))
        {
            var cached = await ResolutionCache.Get(normalization.Hash, ct);
            if (cached is not null)
            {
                location.CanonicalLocationId = cached.CanonicalLocationId;
                location.Metadata["canonical_id"] = cached.CanonicalLocationId;
                await UpdateCanonicalSourcesAsync(cached.CanonicalLocationId, location.SourceSystem, ct);
                _logger.LogDebug("Cache hit for {HashPrefix}", normalization.Hash[..Math.Min(normalization.Hash.Length, 8)]);
                return new ResolutionOutcome(cached.CanonicalLocationId, true, cached.Confidence);
            }
        }

        if (string.IsNullOrEmpty(normalization.Normalized))
        {
            throw new InvalidOperationException("Normalization produced an empty address; cannot continue.");
        }

        var canonical = new CanonicalLocation
        {
            DisplayName = location.Address,
            NormalizedAddress = normalization.Normalized,
            AddressHash = normalization.Hash,
            Latitude = null,
            Longitude = null,
            Attributes = new Dictionary<string, object?>
            {
                ["tokens"] = new Dictionary<string, string>(normalization.Tokens),
                ["sources"] = new List<string> { location.SourceSystem },
                ["confidence"] = normalization.Confidence
            }
        };
        canonical = await canonical.Save(ct);

        if (!string.IsNullOrEmpty(location.SourceSystem) && !string.IsNullOrEmpty(location.SourceId))
        {
            var link = new LocationLink
            {
                Id = LocationLink.BuildId(location.SourceSystem, location.SourceId),
                CanonicalLocationId = canonical.Id,
                SourceSystem = location.SourceSystem,
                SourceId = location.SourceId
            };
            await link.Save(ct);
        }

        var cacheEntry = ResolutionCache.Create(normalization.Hash, normalization.Normalized, canonical.Id, normalization.Confidence);
        await cacheEntry.Save(ct);

        location.CanonicalLocationId = canonical.Id;
        location.Metadata["canonical_id"] = canonical.Id;

        _logger.LogInformation("Resolved location {SourceSystem}/{SourceId} -> {CanonicalId}",
            location.SourceSystem, location.SourceId, canonical.Id);

        return new ResolutionOutcome(canonical.Id, false, normalization.Confidence);
    }

    private async Task<(NormalizationResult Normalization, string CorrectedAddress, string Model)?> TryAiAssistAsync(
        string sourceSystem,
        string originalAddress,
        NormalizationResult baseline,
        CancellationToken ct)
    {
        try
        {
            var ai = Ai.TryResolve();
            if (ai is null)
            {
                return null;
            }

            var model = string.IsNullOrWhiteSpace(_options.AiAssist.Model) ? "mistral" : _options.AiAssist.Model;
            var request = new AiChatRequest
            {
                Model = model,
                Options = new AiPromptOptions { MaxOutputTokens = 160 },
                Messages =
                {
                    new AiMessage("system", "You normalize postal addresses. Return only a single corrected address ready for geocoding."),
                    new AiMessage("user", $"Original address: {originalAddress}"),
                    new AiMessage("user", $"Deterministic normalization produced: {baseline.Normalized}. Improve it if possible.")
                }
            };

            var response = await ai.PromptAsync(request, ct);
            var suggestion = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                return null;
            }

            var improved = _normalizationService.Normalize(sourceSystem, suggestion);
            if (string.IsNullOrEmpty(improved.Normalized) || improved.Confidence <= baseline.Confidence)
            {
                return null;
            }

            var modelUsed = response.Model ?? model;
            return (improved, suggestion, modelUsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI assist failed for address {Address}", originalAddress);
            return null;
        }
    }

    private static async Task UpdateCanonicalSourcesAsync(string canonicalId, string sourceSystem, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(canonicalId) || string.IsNullOrEmpty(sourceSystem))
        {
            return;
        }

        var canonical = await CanonicalLocation.Get(canonicalId, ct);
        if (canonical is null)
        {
            return;
        }

        canonical.Attributes ??= new Dictionary<string, object?>();
        if (!canonical.Attributes.TryGetValue("sources", out var sourcesObj) || sourcesObj is not List<string> sources)
        {
            sources = new List<string>();
            canonical.Attributes["sources"] = sources;
        }

        if (!sources.Exists(s => string.Equals(s, sourceSystem, StringComparison.OrdinalIgnoreCase)))
        {
            sources.Add(sourceSystem);
            await canonical.Save(ct);
        }
    }
}
