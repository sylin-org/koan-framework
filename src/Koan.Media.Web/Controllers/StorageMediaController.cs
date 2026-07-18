using Koan.Media.Web.Infrastructure;
using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Storage.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Koan.Media.Web.Controllers;

/// <summary>
/// CRTP-style base controller that streams stored bytes for a
/// <see cref="Koan.Media.MediaEntity{TEntity}"/>-derived type with full
/// HTTP semantics: HEAD, GET, Range, ETag / If-None-Match,
/// If-Modified-Since, Content-Disposition, Cache-Control.
///
/// <para>This is the "serve original bytes" controller. For
/// transformations on the fly, use <see cref="MediaController"/>
/// (MEDIA-0004 recipe pipeline) at <c>/media/{id}[/{seed}][?params]</c>.
/// The two are orthogonal — apps typically host both:
/// <see cref="StorageMediaController{TEntity}"/>-derived per-entity
/// routes for raw access (eg. <c>/api/previews/{key}</c>) and a single
/// <see cref="MediaController"/> for transform-on-demand.</para>
///
/// <para>Derived controllers customise the route via attribute
/// routing on the subclass. They are not auto-registered; the host
/// app registers each one explicitly.</para>
///
/// <para><b>SECURITY — not access-axis-aware.</b> This controller streams bytes addressed by their storage
/// <i>key</i> via <c>StorageEntity&lt;TEntity&gt;.OpenRead(key)</c>. That path applies STORAGE-layer isolation
/// (the tenant particle, STOR-0011) but NOT the SEC-0008 data-layer access axis — the row-visibility predicate
/// only folds in on an entity read (<c>Data&lt;T&gt;.Get</c>/<c>Query</c>), which this bypasses. Do <b>not</b>
/// subclass this for an <c>[AccessScoped]</c> media type expecting per-subject scoping: a caller with a key
/// would fetch bytes across the access axis (an IDOR). For access-scoped serving use the recipe pipeline
/// (<see cref="MediaController"/> + <see cref="Routing.MediaEntitySource{TEntity}"/>), which resolves through
/// the entity layer. This controller is for raw-bytes access to media that is NOT access-scoped.</para>
/// </summary>
[ApiController]
[Route("api/media")]
public abstract class StorageMediaController<TEntity> : ControllerBase
    where TEntity : class, IStorageObject
{
    private readonly IOptions<StorageMediaOptions> _options;

    protected StorageMediaController() : this(Microsoft.Extensions.Options.Options.Create(new StorageMediaOptions())) { }

    protected StorageMediaController(IOptions<StorageMediaOptions> options)
    {
        _options = options ?? Microsoft.Extensions.Options.Options.Create(new StorageMediaOptions());
    }

    [HttpGet("{**key}")]
    public virtual async Task<IActionResult> Get([FromRoute] string key, CancellationToken ct)
    {
        var stat = await StorageEntity<TEntity>.Head(key, ct);
        if (stat is null) return NotFound();

        // Conditional GET — ETag / If-Modified-Since
        if (Request.Headers.TryGetValue(HttpHeaderNames.IfNoneMatch, out var inm)
            && !string.IsNullOrWhiteSpace(stat.ETag)
            && inm.Any(v => string.Equals(v, stat.ETag, StringComparison.Ordinal)))
        {
            SetEntityHeaders(stat, key);
            return StatusCode(StatusCodes.Status304NotModified);
        }
        if (Request.Headers.TryGetValue(HttpHeaderNames.IfModifiedSince, out var ims)
            && stat.LastModified.HasValue
            && DateTimeOffset.TryParse(ims.ToString(), out var since))
        {
            var lastModUtc = stat.LastModified.Value.ToUniversalTime();
            var lastModTruncated = new DateTimeOffset(
                lastModUtc.Year, lastModUtc.Month, lastModUtc.Day,
                lastModUtc.Hour, lastModUtc.Minute, lastModUtc.Second,
                TimeSpan.Zero);
            if (lastModTruncated <= since.ToUniversalTime())
            {
                SetEntityHeaders(stat, key);
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        // Range request handling
        if (Request.Headers.TryGetValue(HttpHeaderNames.Range, out var rangeHeader) && rangeHeader.Count > 0)
        {
            var (from, to) = ParseRange(rangeHeader.ToString());
            var length = stat.Length;
            if (!length.HasValue)
            {
                Response.Headers[HttpHeaderNames.AcceptRanges] = "bytes";
                Response.Headers[HttpHeaderNames.ContentRange] = "bytes */*";
                return StatusCode(StatusCodes.Status416RequestedRangeNotSatisfiable);
            }

            if (from is null && to is null)
            {
                return await SendFull(key, stat, ct);
            }
            if (from is null)
            {
                var suffixLen = to!.Value;
                if (suffixLen <= 0)
                {
                    SetInvalidRange(length.Value);
                    return StatusCode(StatusCodes.Status416RequestedRangeNotSatisfiable);
                }
                from = Math.Max(0, length.Value - suffixLen);
                to = length.Value - 1;
            }
            if (to is null) to = length.Value - 1;

            if (from.Value < 0 || from.Value >= length.Value || to.Value < from.Value)
            {
                SetInvalidRange(length.Value);
                return StatusCode(StatusCodes.Status416RequestedRangeNotSatisfiable);
            }

            var (stream, _) = await StorageEntity<TEntity>.OpenReadRange(key, from, to, ct);
            var contentLength = (to.Value - from.Value) + 1;
            Response.StatusCode = StatusCodes.Status206PartialContent;
            Response.Headers[HttpHeaderNames.AcceptRanges] = "bytes";
            Response.Headers[HttpHeaderNames.ContentRange] = $"bytes {from}-{to}/{length}";
            SetEntityHeaders(stat, key);
            ConfigureResponse(HttpContext.Response, stat);
            Response.ContentLength = contentLength;
            return File(stream, ResolveContentType(stat, key));
        }

        return await SendFull(key, stat, ct);
    }

    [HttpHead("{**key}")]
    public virtual async Task<IActionResult> Head([FromRoute] string key, CancellationToken ct)
    {
        var stat = await StorageEntity<TEntity>.Head(key, ct);
        if (stat is null) return NotFound();
        Response.Headers[HttpHeaderNames.AcceptRanges] = "bytes";
        SetEntityHeaders(stat, key);
        ConfigureResponse(HttpContext.Response, stat);
        if (stat.Length.HasValue) Response.ContentLength = stat.Length.Value;
        Response.ContentType = ResolveContentType(stat, key);
        return Ok();
    }

    private async Task<IActionResult> SendFull(string key, ObjectStat stat, CancellationToken ct)
    {
        var full = await StorageEntity<TEntity>.OpenRead(key, ct);
        SetEntityHeaders(stat, key);
        Response.Headers[HttpHeaderNames.AcceptRanges] = "bytes";
        ConfigureResponse(HttpContext.Response, stat);
        if (stat.Length.HasValue) Response.ContentLength = stat.Length.Value;
        return File(full, ResolveContentType(stat, key));
    }

    protected virtual string ResolveContentType(ObjectStat stat, string key) =>
        string.IsNullOrWhiteSpace(stat.ContentType) ? GuessContentType(key) : stat.ContentType!;

    protected virtual void SetEntityHeaders(ObjectStat stat, string key)
    {
        if (!string.IsNullOrWhiteSpace(stat.ETag))
            Response.Headers[HttpHeaderNames.ETag] = stat.ETag!;
        if (stat.LastModified.HasValue)
            Response.Headers[HttpHeaderNames.LastModified] = stat.LastModified.Value.ToString("R");
        Response.ContentType = ResolveContentType(stat, key);
    }

    protected virtual void ConfigureResponse(HttpResponse response, ObjectStat stat)
    {
        var opt = _options.Value;
        if (!opt.EnableCacheControl) return;
        if (opt.MaxAge is { } maxAge)
        {
            var visibility = opt.Public ? "public" : "private";
            response.Headers[HttpHeaderNames.CacheControl] =
                $"{visibility}, max-age={(int)maxAge.TotalSeconds}";
        }
    }

    protected virtual string GuessContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".txt" => "text/plain",
            ".json" => "application/json",
            _ => "application/octet-stream",
        };
    }

    private static (long? From, long? To) ParseRange(string header)
    {
        if (string.IsNullOrWhiteSpace(header)) return (null, null);
        var parts = header.Split('=');
        if (parts.Length != 2 || !parts[0].Equals("bytes", StringComparison.OrdinalIgnoreCase))
            return (null, null);
        var range = parts[1].Split('-');
        long? from = long.TryParse(range.ElementAtOrDefault(0), out var f) ? f : null;
        long? to = long.TryParse(range.ElementAtOrDefault(1), out var t) ? t : null;
        return (from, to);
    }

    private void SetInvalidRange(long length)
    {
        Response.Headers[HttpHeaderNames.AcceptRanges] = "bytes";
        Response.Headers[HttpHeaderNames.ContentRange] = $"bytes */{length}";
    }
}

/// <summary>
/// Options for <see cref="StorageMediaController{TEntity}"/> response
/// headers. Bound from <c>Koan:Media:Web:Storage</c>.
/// </summary>
public sealed class StorageMediaOptions
{
    /// <summary>Emit <c>Cache-Control</c> on raw-bytes responses. Default true.</summary>
    public bool EnableCacheControl { get; set; } = true;

    /// <summary>When true, cache visibility is <c>public</c>; otherwise <c>private</c>.</summary>
    public bool Public { get; set; } = true;

    /// <summary>Max-age for the <c>Cache-Control</c> header. Default 1 hour.</summary>
    public TimeSpan? MaxAge { get; set; } = TimeSpan.FromHours(1);
}
