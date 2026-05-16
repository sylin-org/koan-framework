using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Koan.Storage.Abstractions;

namespace Koan.Media.Web.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Koan.Media.Core.Operators;
using Koan.Media.Core.Options;
using Koan.Media.Web.Caching;
using Koan.Media.Web.Infrastructure;
using Koan.Media.Web.Options;
using Koan.Storage;
using Koan.Storage.Model;

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

        // Transform pipeline: when the request carries any operator-recognised query param, route
        // through the operator chain instead of the raw-bytes path. Operators self-skip when no
        // relevant params are present, so this only fires when the caller actually asks for it.
        var (transformResult, transformDispatched) = await TryServeTransformed(key, stat, ct);
        if (transformDispatched) return transformResult!;

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
                return await SendFull(key, stat, ct);
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

    /// <summary>
    /// Apply the registered operator chain when the request carries any matching param. Returns
    /// <c>(result, true)</c> on dispatch (cached hit, fresh transform, or 304); <c>(null, false)</c>
    /// when no operator claimed any param and the caller should fall through to the raw-bytes path.
    /// </summary>
    private async Task<(IActionResult? Result, bool Handled)> TryServeTransformed(string key, ObjectStat stat, CancellationToken ct)
    {
        var registry = HttpContext.RequestServices.GetService<IMediaOperatorRegistry>();
        if (registry is null) return (null, false);

        var optionsMonitor = HttpContext.RequestServices.GetService<IOptionsMonitor<MediaTransformOptions>>();
        var pipelineOptions = optionsMonitor?.CurrentValue ?? new MediaTransformOptions();

        var contentType = ResolveContentType(stat, key);
        var query = new Dictionary<string, StringValues>(Request.Query, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<(IMediaOperator Op, IReadOnlyDictionary<string, string> Params)> chain;
        try
        {
            chain = registry.ResolveOperators(query, stat, contentType, pipelineOptions);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Operator-level validation failure (strict mode) — surface as 400 so callers can fix
            // their query string rather than getting a noisy 500.
            return (BadRequest(new { error = ex.Message }), true);
        }
        if (chain.Count == 0) return (null, false);

        var cacheKey = BuildCacheKey(key, chain);
        var etag = $"\"{cacheKey}\"";

        // Conditional GET on the transformed output. The ETag is the deterministic cacheKey, so a
        // browser that already has the rendered thumbnail can short-circuit even on a cold server.
        if (Request.Headers.TryGetValue(HttpHeaderNames.IfNoneMatch, out var inm) &&
            inm.Any(v => string.Equals(v, etag, StringComparison.Ordinal)))
        {
            Response.Headers[HttpHeaderNames.ETag] = etag;
            return (StatusCode(304), true);
        }

        var cache = HttpContext.RequestServices.GetService<IMediaTransformCache>();
        if (cache is not null)
        {
            var hit = await cache.TryGetAsync(cacheKey, ct);
            if (hit is not null)
            {
                return (ServeBytes(hit.Bytes, hit.ContentType, etag), true);
            }
        }

        // Cache miss (or no cache registered) — run the pipeline. Each operator reads from a
        // MemoryStream and writes to a fresh MemoryStream; we swap roles between stages so the
        // final stream holds the encoded output.
        await using var source = await StorageEntity<TEntity>.OpenRead(key, ct);
        var inputStream = await BufferAsync(source, ct);
        var currentContentType = contentType;
        foreach (var (op, parameters) in chain)
        {
            var output = new MemoryStream();
            (string? newCt, _) = await op.Execute(inputStream, currentContentType, output, parameters, pipelineOptions, ct);
            output.Position = 0;
            await inputStream.DisposeAsync();
            inputStream = output;
            if (!string.IsNullOrWhiteSpace(newCt)) currentContentType = newCt!;
        }

        var bytes = inputStream is MemoryStream ms ? ms.ToArray() : await BufferToBytesAsync(inputStream, ct);
        await inputStream.DisposeAsync();

        if (cache is not null)
        {
            await cache.WriteAsync(cacheKey, new MediaCacheEntry(bytes, currentContentType, etag), ct);
        }

        return (ServeBytes(bytes, currentContentType, etag), true);
    }

    private IActionResult ServeBytes(byte[] bytes, string contentType, string etag)
    {
        Response.Headers[HttpHeaderNames.ETag] = etag;
        // Transformed output is a deterministic function of (key, params). Both pieces are baked
        // into the URL (key in the path, params in the query string), so safe to cache aggressively
        // — immutable hint asks browsers/intermediaries to skip revalidation entirely.
        var opt = _options.Value;
        if (opt.EnableCacheControl)
        {
            var visibility = opt.Public ? "public" : "private";
            var maxAge = opt.MaxAge?.TotalSeconds ?? TimeSpan.FromDays(365).TotalSeconds;
            Response.Headers[HttpHeaderNames.CacheControl] = $"{visibility}, max-age={(int)maxAge}, immutable";
        }
        return File(bytes, contentType);
    }

    private static async Task<MemoryStream> BufferAsync(Stream source, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    private static async Task<byte[]> BufferToBytesAsync(Stream source, CancellationToken ct)
    {
        if (source.CanSeek) source.Position = 0;
        using var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>
    /// Deterministic cache key: <c>{key}#{op:param=val,param=val|op:...}</c>, where operators run
    /// in pipeline order and params are sorted by name. Aliased query params (e.g. <c>w</c> vs
    /// <c>width</c>) collapse to the canonical form because the registry normalized them.
    /// </summary>
    private static string BuildCacheKey(string key, IReadOnlyList<(IMediaOperator Op, IReadOnlyDictionary<string, string> Params)> chain)
    {
        var sb = new StringBuilder();
        sb.Append(key);
        sb.Append('#');
        for (var i = 0; i < chain.Count; i++)
        {
            if (i > 0) sb.Append('|');
            sb.Append(chain[i].Op.Id);
            sb.Append(':');
            var first = true;
            foreach (var kv in chain[i].Params.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(kv.Key);
                sb.Append('=');
                sb.Append(kv.Value);
            }
        }
        // Hash the human-readable form to a short, safe token. The raw form would already be a
        // valid ETag; hashing trims the header bytes and avoids querystring-length surprises.
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
