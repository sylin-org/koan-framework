using System.Text.Json.Nodes;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;
using Koan.Media.Core.Recipes;
using Koan.Media.Web.Infrastructure;
using Koan.Media.Web.Negotiation;
using Koan.Media.Web.Options;
using Koan.Media.Web.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Koan.Media.Web.Controllers;

/// <summary>
/// HTTP surface for the recipe pipeline. Implements MEDIA-0004 §8/§9.
/// Single controller, three concerns:
/// <list type="number">
///   <item><c>GET /media/{id}</c> — original bytes, format-preserved</item>
///   <item><c>GET /media/{id}/{seed}</c> — apply named recipe or format shortcut, with query overrides</item>
///   <item><c>GET /media/recipes[/{name}][?as=appsettings]</c> — introspection</item>
/// </list>
///
/// Content-addressable URL (<c>/media/{id}@{hash}/...</c>) and signing
/// land in follow-up phases per the ADR migration plan. This controller
/// exposes the base grammar and override layering today.
///
/// <para>Per MEDIA-0007, derivations are persisted through the registered
/// <see cref="IMediaSource"/> directly — the same storage namespace as the
/// originals, with lineage stamped on <c>SourceMediaId</c>/<c>DerivationKey</c>/
/// <c>RelationshipType</c>/<c>Tags["recipe-version"]</c>. When the source does
/// not persist derivations, every request renders from scratch.
/// </para>
/// </summary>
[ApiController]
public sealed class MediaController : ControllerBase
{
    private readonly IMediaRecipeRegistry _registry;
    private readonly IMediaSource _source;
    private readonly IOverlayResolver? _overlayResolver;
    private readonly Koan.Media.Core.Fonts.KoanFontRegistry? _fonts;
    private readonly MediaWebOptions _options;
    private readonly ILogger<MediaController> _logger;
    private readonly IServiceProvider _services;

    public MediaController(
        IMediaRecipeRegistry registry,
        IMediaSource source,
        IOptions<MediaWebOptions> options,
        ILogger<MediaController> logger,
        IServiceProvider services,
        IOverlayResolver? overlayResolver = null,
        Koan.Media.Core.Fonts.KoanFontRegistry? fonts = null)
    {
        _registry = registry;
        _source = source;
        _overlayResolver = overlayResolver;
        _options = options.Value;
        _logger = logger;
        _services = services;
        // Lazy-apply any AddKoanFont() registrations queued before AddKoan() ran
        if (fonts is not null)
        {
            _fonts = fonts;
            Koan.Media.Core.Fonts.ServiceCollectionFontExtensions.ApplyPendingFonts(_fonts, services);
        }
    }

    // ----- Recipe introspection -----

    [HttpGet("media/recipes")]
    public IActionResult ListRecipes()
    {
        var payload = RecipeJsonSerializer.SerializeAll(_registry.All, _registry.FormatShortcuts);
        return Content(payload.ToJsonString(), "application/json");
    }

    [HttpGet("media/recipes/{name}")]
    public IActionResult GetRecipe(string name, [FromQuery(Name = "as")] string? asFormat = null)
    {
        var recipe = _registry.Find(name);
        if (recipe is null) return NotFound(new { error = $"Recipe '{name}' not found." });

        if (string.Equals(asFormat, "appsettings", StringComparison.OrdinalIgnoreCase))
        {
            var wrapped = RecipeJsonSerializer.SerializeAsAppSettings(recipe);
            return Content(RecipeJsonSerializer.ToIndentedString(wrapped), "application/json");
        }
        var single = RecipeJsonSerializer.Serialize(recipe);
        return Content(single.ToJsonString(), "application/json");
    }

    // ----- Media rendering -----

    /// <summary>Original bytes, format-preserved. Equivalent to <c>?</c> with no params.</summary>
    [HttpGet("media/{id}")]
    public Task<IActionResult> GetOriginal(string id, CancellationToken ct)
        => RenderAsync(id, seed: null, ct: ct);

    /// <summary>Named recipe or format-shortcut render with optional overrides.</summary>
    [HttpGet("media/{id}/{seed}")]
    public Task<IActionResult> GetWithSeed(string id, string seed, CancellationToken ct)
        => RenderAsync(id, seed, ct);

    // ----- Engine -----

    private async Task<IActionResult> RenderAsync(string id, string? seed, CancellationToken ct)
    {
        // 1) Resolve seed
        MediaRecipe? seedRecipe = null;
        var seedIsFormatShortcut = false;
        if (!string.IsNullOrWhiteSpace(seed))
        {
            if (!_registry.TryResolve(seed, out var resolved))
            {
                return NotFound(new { error = $"Unknown recipe or format shortcut '{seed}'." });
            }
            seedRecipe = resolved;
            // Per MEDIA-0009 §f: format-shortcut URLs bypass negotiation
            // entirely. The registry synthesises a format-shortcut recipe
            // with Source = AdHoc and a pinned EncodeStep.Format; the
            // controller honours that pin and suppresses Vary: Accept.
            seedIsFormatShortcut = resolved.Source == RecipeSource.AdHoc;
        }

        // 2) Parse query params into an effective recipe
        var queryParams = Request.Query.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        // Drop control params that aren't pipeline-related
        queryParams.Remove("as");

        var parseResult = MediaUrlParser.Parse(
            seedRecipe,
            queryParams,
            adHocAllowed: _options.AllowAdHoc,
            strict: _options.StrictUnknownParams);

        if (parseResult.HasRejections)
        {
            return BadRequest(new
            {
                error = "One or more query parameters are not accepted by this recipe.",
                rejected = parseResult.RejectedParams,
                recipe = seedRecipe?.Name,
                allowedMutators = seedRecipe is null ? null : EnumerateMutators(seedRecipe.AllowedMutators),
            });
        }

        // 3) Enforce output dimension limit
        if (ExceedsDimensionLimit(parseResult.Recipe, _options.MaxOutputEdge, out var offending))
        {
            Response.Headers["X-Koan-Media-LimitExceeded"] = "maxOutputEdge";
            return BadRequest(new
            {
                error = $"Output dimension {offending} exceeds limit {_options.MaxOutputEdge}px.",
                limit = "maxOutputEdge",
                value = offending,
            });
        }

        // 4) Resolve source bytes
        var handle = await _source.OpenAsync(id, ct).ConfigureAwait(false);
        if (handle is null) return NotFound(new { error = $"Media '{id}' not found." });

        try
        {
            var effectiveRecipe = parseResult.Recipe;

            // 4a) No-transform fast path. When the URL carries no recipe
            // name, no mutator query params, and the parsed recipe has
            // no AllowedOutputFormats negotiation surface, there is
            // literally nothing for the pipeline to do - skip the
            // decode-and-encode entirely and stream the source bytes
            // verbatim with their stored ContentType. This is what
            // makes `/media/{id}` work for non-image content
            // (video, archives, ...) without the image pipeline
            // throwing MediaDecodeException; a recipe URL still routes
            // through the pipeline below and will surface a typed
            // error for non-image sources requested through an
            // image-transform recipe.
            //
            // "No transforms" here means: no AllowedOutputFormats
            // negotiation surface AND the recipe's step list is either
            // empty OR contains only the implicit format-preserving
            // EncodeStep that MediaRecipeBuilder.Build() injects when
            // the caller didn't declare one (EncodeStep with
            // Format: null). Any other step (Resize from ?w=, Shape,
            // Sample, etc.) means a real transform was requested.
            //
            // ETag keys raw responses under a "raw" suffix so a cached
            // raw byte response can't poison a future recipe response
            // at the same source id.
            var hasOnlyImplicitEncode = effectiveRecipe.Steps.Length == 0
                || (effectiveRecipe.Steps.Length == 1
                    && effectiveRecipe.Steps[0] is EncodeStep es
                    && es.Format is null);
            if (hasOnlyImplicitEncode
                && effectiveRecipe.AllowedOutputFormats.IsDefaultOrEmpty)
            {
                var rawEtag = BuildETag(handle.ContentHashHex, "raw");
                if (Request.Headers.TryGetValue(HttpHeaderNames.IfNoneMatch, out var rawIfNone)
                    && rawIfNone.ToString().Contains(rawEtag, StringComparison.Ordinal))
                {
                    Response.Headers[HeaderNames.ETag] = rawEtag;
                    Response.Headers[HttpHeaderNames.CacheControl] = _options.DefaultCacheControl;
                    return StatusCode(StatusCodes.Status304NotModified);
                }
                Response.Headers[HeaderNames.ETag] = rawEtag;
                Response.Headers[HttpHeaderNames.CacheControl] = _options.DefaultCacheControl;
                if (handle.LastModified is { } lm)
                {
                    Response.Headers[HeaderNames.LastModified] = lm.ToString("R");
                }
                Response.ContentType = handle.ContentType;
                // Stream directly to Response.Body so the outer finally's
                // handle.DisposeAsync() doesn't race with FileStreamResult
                // reading the stream after the action returns.
                await handle.Bytes.CopyToAsync(Response.Body, ct).ConfigureAwait(false);
                return new EmptyResult();
            }

            // Per MEDIA-0009 §d/e: when the recipe declares an
            // AllowedOutputFormats allowlist and the seed wasn't a
            // format-shortcut URL, negotiate the output format from the
            // request's Accept header against the encoder registry, then
            // inject the negotiated slug as a synthetic EncodeAs step.
            // The fingerprint is computed AFTER injection so the cache
            // key naturally folds in the negotiated format — two
            // (source, recipe) pairs with different Accept headers
            // produce distinct cache entries with no cross-format
            // poisoning.
            var negotiationHappened = false;
            if (!seedIsFormatShortcut
                && !effectiveRecipe.AllowedOutputFormats.IsDefaultOrEmpty
                && effectiveRecipe.AllowedOutputFormats.Length > 0)
            {
                var acceptHeader = Request.Headers.TryGetValue(HeaderNames.Accept, out var rawAccept)
                    ? rawAccept.ToString()
                    : null;
                var negotiated = FormatNegotiator.Negotiate(
                    effectiveRecipe.AllowedOutputFormats,
                    acceptHeader,
                    sourceFormat: string.Empty);
                effectiveRecipe = InjectSyntheticEncode(effectiveRecipe, negotiated);
                negotiationHappened = true;
            }

            var fingerprint = effectiveRecipe.Fingerprint();
            var etag = BuildETag(handle.ContentHashHex, fingerprint);

            // 5) Conditional GET — short-circuit on If-None-Match
            if (Request.Headers.TryGetValue(HttpHeaderNames.IfNoneMatch, out var ifNoneMatch) &&
                ifNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
            {
                ApplyDiagnostics(seedRecipe, effectiveRecipe, fingerprint, sourceFormat: null,
                    output: null, ignored: parseResult.IgnoredParams, fromCache: "hit");
                Response.Headers[HeaderNames.ETag] = etag;
                Response.Headers[HttpHeaderNames.CacheControl] = _options.DefaultCacheControl;
                return StatusCode(StatusCodes.Status304NotModified);
            }

            // 5b) Storage-backed derivation lookup — serve a previously persisted
            // render and skip the resize/re-encode pipeline. Per MEDIA-0007 the
            // derivation lives under the same storage profile as the source,
            // keyed by (sourceId, recipeFingerprint).
            var derivation = await _source
                .OpenDerivationAsync(id, fingerprint, ct)
                .ConfigureAwait(false);
            if (derivation is not null)
            {
                Response.Headers[HeaderNames.ETag] = etag;
                Response.Headers[HttpHeaderNames.CacheControl] = _options.DefaultCacheControl;
                ApplyDiagnostics(seedRecipe, effectiveRecipe, fingerprint,
                    sourceFormat: null, output: null,
                    ignored: parseResult.IgnoredParams, fromCache: "hit");
                return File(derivation.Bytes, derivation.ContentType);
            }

            // 6) Run pipeline. Per MEDIA-0008 the encoder writes through
            // the pipeline's streaming terminal — WriteToAsync — instead
            // of materialising a full byte buffer. Storage write-through
            // still consumes bytes today (its contract predates MEDIA-0008),
            // so we tee the encode through a MemoryStream and reuse the
            // captured buffer for the response body. The streaming win is
            // realised immediately at the encoder boundary; the controller-level
            // tee will be replaced with a temp-file/Stream contract in a
            // follow-up ADR once IMediaSource.TryStoreDerivationAsync
            // accepts a writer instead of a MediaOutput.
            MediaOutput output;
            byte[] capturedBytes;
            try
            {
                await using var buffer = new MemoryStream();
                output = await handle.Bytes
                    .AsMedia(_logger, _overlayResolver, _fonts, new MediaPipelineLimits
                    {
                        MaxSourceMegapixels = _options.MaxSourceMegapixels,
                        MaxFrameCount = _options.MaxFrameCount,
                    })
                    .Apply(effectiveRecipe)
                    .WriteToAsync(buffer, ct)
                    .ConfigureAwait(false);
                capturedBytes = buffer.ToArray();
            }
            catch (MediaSourceLimitException lex)
            {
                Response.Headers["X-Koan-Media-LimitExceeded"] = lex.LimitName;
                return BadRequest(new
                {
                    error = lex.Message,
                    limit = lex.LimitName,
                    value = lex.Value,
                    cap = lex.Cap,
                });
            }
            catch (MediaDecodeException dex)
            {
                return UnprocessableEntity(new { error = dex.Message });
            }

            // Promote the captured bytes onto the MediaOutput so the
            // storage write-through — which still consumes MediaOutput.Bytes
            // — sees the populated buffer.
#pragma warning disable CS0618 // MediaOutput.Bytes obsolete on the streaming path; required for MEDIA-0007 storage write-through until its contract migrates to a writer.
            var bufferedOutput = output with
            {
                Bytes = capturedBytes,
                WriteToAsync = (dest, dct) => dest.WriteAsync(capturedBytes, dct).AsTask(),
            };
#pragma warning restore CS0618

            // 6b) Write-through to durable storage. Best-effort: the source
            // implementation swallows IO errors so a failure here never faults
            // the response. Lineage fields are stamped at write time per
            // MEDIA-0007 §b.
            var recipeName = seedRecipe?.Name ?? effectiveRecipe.Name;
            var recipeVersion = (seedRecipe?.Version ?? effectiveRecipe.Version)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                await _source.TryStoreDerivationAsync(
                    id, fingerprint, bufferedOutput, recipeName, recipeVersion, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Derivation write-through failed for {Id}/{Fingerprint}", id, fingerprint);
            }

            // 7) Build response
            Response.Headers[HeaderNames.ETag] = etag;
            Response.Headers[HttpHeaderNames.CacheControl] = _options.DefaultCacheControl;
            // Per MEDIA-0009 §f: Vary: Accept is precise.
            //   - Format-shortcut URL → no Vary (URL pinned it).
            //   - Single-format allowlist → no Vary (only one possible output).
            //   - Multi-format allowlist → Vary: Accept (negotiation could differ per request).
            //   - No allowlist, format pinned by recipe → no Vary (today's behavior).
            //   - No allowlist, no pinned format → Vary: Accept (source-preserve can still differ per kind).
            if (ShouldEmitVaryAccept(effectiveRecipe, parseResult.Recipe, seedIsFormatShortcut, negotiationHappened))
            {
                Response.Headers[HttpHeaderNames.Vary] = "Accept";
            }

            ApplyDiagnostics(seedRecipe, effectiveRecipe, fingerprint,
                sourceFormat: bufferedOutput.SourceFormat, output: bufferedOutput,
                ignored: parseResult.IgnoredParams, fromCache: "miss");

            // Per MEDIA-0008 the controller threads the bytes through the
            // streaming WriteToAsync on the MediaOutput. With the captured
            // buffer in hand we replay it onto Response.Body via that same
            // contract — the future no-tee path uses the encoder's own
            // SaveAsync into Response.Body without ever allocating bytes.
            Response.ContentType = bufferedOutput.ContentType;
            await bufferedOutput.WriteToAsync(Response.Body, ct).ConfigureAwait(false);
            return new EmptyResult();
        }
        finally
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ApplyDiagnostics(
        MediaRecipe? seedRecipe,
        MediaRecipe effectiveRecipe,
        string fingerprint,
        string? sourceFormat,
        MediaOutput? output,
        IReadOnlyList<string> ignored,
        string fromCache)
    {
        Response.Headers[HttpHeaderNames.XKoanMediaRecipe] =
            seedRecipe?.Name ?? effectiveRecipe.Name ?? "ad-hoc";
        Response.Headers[HttpHeaderNames.XKoanMediaRecipeHash] = fingerprint;
        if (output is not null)
        {
            Response.Headers[HttpHeaderNames.XKoanMediaSourceFormat] = sourceFormat ?? output.Format;
            Response.Headers[HttpHeaderNames.XKoanMediaOutputFormat] = output.Format;
            Response.Headers[HttpHeaderNames.XKoanMediaFrameCount] =
                output.FrameCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (output.KindTrace.Count > 0)
            {
                // Per MEDIA-0005 §7: human-readable kind transitions.
                // Format: "Raster -> Raster -> Raster" (kinds joined by " -> ").
                Response.Headers[HttpHeaderNames.XKoanMediaKindTrace] =
                    string.Join(" -> ", output.KindTrace.Select(k => k.ToString()));
            }
        }
        Response.Headers["X-Koan-Media-FromCache"] = fromCache;
        if (ignored.Count > 0)
        {
            Response.Headers[HttpHeaderNames.XKoanMediaIgnoredParams] = string.Join(",", ignored);
        }
    }

    private static string BuildETag(string sourceHash, string recipeFingerprint)
    {
        var sourceShort = sourceHash.Length >= 12 ? sourceHash[..12] : sourceHash;
        return $"\"{sourceShort}-{recipeFingerprint}\"";
    }

    private static bool ExceedsDimensionLimit(MediaRecipe recipe, int maxEdge, out int offending)
    {
        offending = 0;
        foreach (var step in recipe.Steps)
        {
            if (step is ResizeStep rz)
            {
                var w = rz.Width.HasValue ? (int)Math.Round(rz.Width.Value * Math.Max(1.0, rz.Dpr)) : 0;
                var h = rz.Height.HasValue ? (int)Math.Round(rz.Height.Value * Math.Max(1.0, rz.Dpr)) : 0;
                if (w > maxEdge) { offending = w; return true; }
                if (h > maxEdge) { offending = h; return true; }
            }
            if (step is ShapeStep ss && ss.Crop is { } crop && crop.Kind != CropSpecKind.Aspect)
            {
                if (crop.Width > maxEdge) { offending = crop.Width; return true; }
                if (crop.Height > maxEdge) { offending = crop.Height; return true; }
            }
        }
        return false;
    }

    /// <summary>
    /// Per MEDIA-0009 §e: inject the negotiated format as a synthetic
    /// <see cref="EncodeStep"/> so the fingerprint folds in the format
    /// slug. The synthetic step replaces any existing
    /// <see cref="EncodeStep"/> in the recipe (single-slot per
    /// <see cref="PipelineStage.Encode"/>) and preserves the original
    /// step's quality when one was already declared.
    /// </summary>
    private static MediaRecipe InjectSyntheticEncode(MediaRecipe recipe, string negotiatedFormat)
    {
        // Carry the existing encode step's quality forward; otherwise
        // use the Quality.Web default.
        var quality = Quality.Web;
        var existingIndex = -1;
        for (var i = 0; i < recipe.Steps.Length; i++)
        {
            if (recipe.Steps[i] is EncodeStep encode)
            {
                quality = encode.Quality;
                existingIndex = i;
                break;
            }
        }

        var synthetic = new EncodeStep(Format: negotiatedFormat, Quality: quality);
        var rebuilt = existingIndex >= 0
            ? recipe.Steps.SetItem(existingIndex, synthetic)
            : recipe.Steps.Add(synthetic);
        return recipe with { Steps = rebuilt };
    }

    /// <summary>
    /// Per MEDIA-0009 §f: precise <c>Vary: Accept</c> rules.
    /// </summary>
    /// <param name="effectiveRecipe">
    /// The recipe after URL overrides and (optionally) the synthetic
    /// negotiated-encode injection. Used to inspect the actual pinned
    /// format on the wire.
    /// </param>
    /// <param name="declaredRecipe">
    /// The recipe before negotiation injection — used to read the
    /// original <see cref="MediaRecipe.AllowedOutputFormats"/> length
    /// so a multi-format allowlist still emits <c>Vary</c> even though
    /// the synthetic step has pinned the format on this request.
    /// </param>
    /// <param name="seedIsFormatShortcut">True when the URL was a format-shortcut.</param>
    /// <param name="negotiationHappened">True when the negotiator ran.</param>
    private static bool ShouldEmitVaryAccept(
        MediaRecipe effectiveRecipe,
        MediaRecipe declaredRecipe,
        bool seedIsFormatShortcut,
        bool negotiationHappened)
    {
        // Format-shortcut URL: operator pinned it, no Vary.
        if (seedIsFormatShortcut) return false;

        // Multi-format allowlist: negotiation ran and the response
        // could legitimately differ on Accept — emit Vary.
        if (negotiationHappened)
        {
            return declaredRecipe.AllowedOutputFormats.Length > 1;
        }

        // No allowlist path: today's behavior — Vary iff no pinned format.
        var formatPinned = effectiveRecipe.Steps.OfType<EncodeStep>().Any(e => e.Format is not null)
            || effectiveRecipe.Steps.OfType<FlattenToStep>().Any();
        return !formatPinned;
    }

    private static IEnumerable<string> EnumerateMutators(MutatorKind kinds)
    {
        foreach (MutatorKind kind in Enum.GetValues<MutatorKind>())
        {
            if (kind == MutatorKind.None) continue;
            if (kind == MutatorKind.All || kind == MutatorKind.Common) continue;
            if ((kinds & kind) == kind) yield return kind.ToString().ToLowerInvariant();
        }
    }
}
