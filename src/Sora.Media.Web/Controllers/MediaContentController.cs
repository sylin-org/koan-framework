using Sora.Storage.Abstractions;

namespace Sora.Media.Web.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sora.Media.Web.Infrastructure;
using Sora.Media.Web.Options;
using Sora.Storage;
using Sora.Storage.Model;

[ApiController]
[Route("api/media")] // Consumers can re-route via MapControllerRoute or attribute routing in derived class
public abstract class MediaContentController<TEntity> : ControllerBase where TEntity : class, IStorageObject
{
    private readonly IOptions<MediaContentOptions> _options;

    protected MediaContentController()
    {
        _options = Options.Create(new MediaContentOptions());
    }

    protected MediaContentController(IOptions<MediaContentOptions> options)
    {
        _options = options ?? Options.Create(new MediaContentOptions());
    }

    [HttpGet("{**key}")]
    public virtual async Task<IActionResult> Get([FromRoute] string key, CancellationToken ct)
    {
        // Pre-fetch stat for content negotiation and conditional requests
        var stat = await StorageEntity<TEntity>.Head(key, ct);
        if (stat is null) return NotFound();

        // Conditional GET with ETag / If-Modified-Since
        if (Request.Headers.TryGetValue(HttpHeaderNames.IfNoneMatch, out var inm) && !string.IsNullOrWhiteSpace(stat.ETag))
        {
            if (inm.Any(v => string.Equals(v, stat.ETag, StringComparison.Ordinal)))
            {
                SetEntityHeaders(stat, key);
                return StatusCode(304);
            }
        }
        if (Request.Headers.TryGetValue(HttpHeaderNames.IfModifiedSince, out var ims) && stat.LastModified.HasValue)
        {
            if (DateTimeOffset.TryParse(ims.ToString(), out var since))
            {
                var sinceUtc = since.ToUniversalTime();
                var lastModUtc = stat.LastModified.Value.ToUniversalTime();
                // Truncate Last-Modified to seconds to align with RFC1123 resolution
                var lastModTruncated = new DateTimeOffset(lastModUtc.Year, lastModUtc.Month, lastModUtc.Day, lastModUtc.Hour, lastModUtc.Minute, lastModUtc.Second, TimeSpan.Zero);
                if (lastModTruncated <= sinceUtc)
                {
                    SetEntityHeaders(stat, key);
                    return StatusCode(304);
                }
            }
        }

        // Range request handling
        if (Request.Headers.TryGetValue(HttpHeaderNames.Range, out var rangeHeader) && rangeHeader.Count > 0)
        {
            var (from, to) = ParseRange(rangeHeader.ToString());
            var length = stat.Length;
            if (!length.HasValue)
            {
                // Can't satisfy a range request without length; respond 416 per RFC
                Response.Headers[HttpHeaderNames.AcceptRanges] = "bytes";
                Response.Headers[HttpHeaderNames.ContentRange] = "bytes */*";
                return StatusCode(416);
            }

            // Normalize missing ends
            if (from is null && to is null)
            {
                // No valid range parsed; treat as full content
                return await SendFullAsync(key, stat, ct);
            }

            if (from is null)
            {
                // suffix range: last 'to' bytes
                var suffixLen = to!.Value;
                if (suffixLen <= 0)
                {
                    SetInvalidRange(length.Value);
                    return StatusCode(416);
                }
                from = Math.Max(0, length.Value - suffixLen);
                to = length.Value - 1;
            }

            if (to is null) to = length.Value - 1;

            // Validate range
            if (from.Value < 0 || from.Value >= length.Value || to.Value < from.Value)
            {
                SetInvalidRange(length.Value);
                return StatusCode(416);
            }

            var (stream, _) = await StorageEntity<TEntity>.OpenReadRange(key, from, to, ct);
            var contentLength = (to.Value - from.Value) + 1;
            Response.StatusCode = 206;
            Response.Headers[HttpHeaderNames.AcceptRanges] = "bytes";
            Response.Headers[HttpHeaderNames.ContentRange] = $"bytes {from}-{to}/{length}";
            SetEntityHeaders(stat, key);
            ConfigureResponse(HttpContext.Response, stat);
            Response.ContentLength = contentLength;
            return File(stream, ResolveContentType(stat, key));
        }

        return await SendFullAsync(key, stat, ct);
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

    private async Task<IActionResult> SendFullAsync(string key, ObjectStat stat, CancellationToken ct)
    {
        var full = await StorageEntity<TEntity>.OpenRead(key, ct);
        SetEntityHeaders(stat, key);
        Response.Headers[HttpHeaderNames.AcceptRanges] = "bytes";
        ConfigureResponse(HttpContext.Response, stat);
        if (stat.Length.HasValue) Response.ContentLength = stat.Length.Value;
        return File(full, ResolveContentType(stat, key));
    }

    protected virtual string ResolveContentType(ObjectStat stat, string key)
        => string.IsNullOrWhiteSpace(stat.ContentType) ? GuessContentType(key) : stat.ContentType!;

    protected virtual void SetEntityHeaders(ObjectStat stat, string key)
    {
        if (!string.IsNullOrWhiteSpace(stat.ETag)) Response.Headers[HttpHeaderNames.ETag] = stat.ETag!;
        if (stat.LastModified.HasValue) Response.Headers[HttpHeaderNames.LastModified] = stat.LastModified.Value.ToString("R");
        Response.ContentType = ResolveContentType(stat, key);
    }

    protected virtual void ConfigureResponse(Microsoft.AspNetCore.Http.HttpResponse response, ObjectStat stat)
    {
        var opt = _options.Value;
        if (!opt.EnableCacheControl) return;
        if (opt.MaxAge is { } maxAge)
        {
            var visibility = opt.Public ? "public" : "private";
            response.Headers[HttpHeaderNames.CacheControl] = $"{visibility}, max-age={(int)maxAge.TotalSeconds}";
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
            _ => "application/octet-stream"
        };
    }

    private static (long? From, long? To) ParseRange(string header)
    {
        if (string.IsNullOrWhiteSpace(header)) return (null, null);
        var parts = header.Split('=');
        if (parts.Length != 2 || !parts[0].Equals("bytes", StringComparison.OrdinalIgnoreCase)) return (null, null);
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
