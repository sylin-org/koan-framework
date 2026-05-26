using System.Text.Json.Nodes;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;
using Koan.Media.Core.Recipes;
using Koan.Media.Web.Infrastructure;
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
/// </summary>
[ApiController]
public sealed class MediaController : ControllerBase
{
    private readonly IMediaRecipeRegistry _registry;
    private readonly IMediaSource _source;
    private readonly MediaWebOptions _options;
    private readonly ILogger<MediaController> _logger;

    public MediaController(
        IMediaRecipeRegistry registry,
        IMediaSource source,
        IOptions<MediaWebOptions> options,
        ILogger<MediaController> logger)
    {
        _registry = registry;
        _source = source;
        _options = options.Value;
        _logger = logger;
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
        if (!string.IsNullOrWhiteSpace(seed))
        {
            if (!_registry.TryResolve(seed, out var resolved))
            {
                return NotFound(new { error = $"Unknown recipe or format shortcut '{seed}'." });
            }
            seedRecipe = resolved;
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

            // 6) Run pipeline
            MediaOutput output;
            try
            {
                output = await handle.Bytes.AsMedia(_logger).Apply(effectiveRecipe).ToBytesAsync(ct).ConfigureAwait(false);
            }
            catch (MediaDecodeException dex)
            {
                return UnprocessableEntity(new { error = dex.Message });
            }

            // 7) Build response
            Response.Headers[HeaderNames.ETag] = etag;
            Response.Headers[HttpHeaderNames.CacheControl] = _options.DefaultCacheControl;
            // Vary: Accept when the recipe did not pin a format AND the seed was empty
            // (i.e. format negotiation could legitimately differ).
            var formatPinned = effectiveRecipe.Steps.OfType<EncodeStep>().Any(e => e.Format is not null)
                || effectiveRecipe.Steps.OfType<FlattenToStep>().Any();
            if (!formatPinned)
            {
                Response.Headers[HttpHeaderNames.Vary] = "Accept";
            }

            ApplyDiagnostics(seedRecipe, effectiveRecipe, fingerprint,
                sourceFormat: output.Format, output: output,
                ignored: parseResult.IgnoredParams, fromCache: "miss");

            return File(output.Bytes, output.ContentType);
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
