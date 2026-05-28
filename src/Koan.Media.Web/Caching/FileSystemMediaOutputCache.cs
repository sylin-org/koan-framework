using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Koan.Media.Web.Caching;

/// <summary>
/// Filesystem-backed <see cref="IMediaOutputCache"/>. Stores each render as a
/// single file <c>{root}/{shard}/{id}-{fingerprint}.{ext}</c>, where the
/// extension is the canonical format slug (so the content-type round-trips
/// without a sidecar) and <c>shard</c> is the id's first two chars (keeps any
/// one directory from collecting every entry).
///
/// <para>All IO is wrapped: reads degrade to a miss, writes are best-effort.
/// Writes go to a temp file then atomic-rename so a concurrent reader never
/// observes a half-written blob.</para>
/// </summary>
internal sealed class FileSystemMediaOutputCache : IMediaOutputCache
{
    private readonly string _root;
    private readonly ILogger _logger;

    public FileSystemMediaOutputCache(string root, ILogger logger)
    {
        _root = root;
        _logger = logger;
        try
        {
            Directory.CreateDirectory(_root);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media output cache root could not be created: {Root}", _root);
        }
    }

    public Task<MediaCacheHit?> TryGetAsync(string id, string fingerprint, CancellationToken ct = default)
    {
        if (!IsSafeKeyPart(id) || !IsSafeKeyPart(fingerprint))
        {
            return Task.FromResult<MediaCacheHit?>(null);
        }

        try
        {
            var dir = ShardDir(id);
            if (!Directory.Exists(dir)) return Task.FromResult<MediaCacheHit?>(null);

            // Format isn't known until render time, so the extension is unknown
            // on read — match the single entry for this (id, fingerprint) pair.
            string? match = null;
            foreach (var file in Directory.EnumerateFiles(dir, $"{id}-{fingerprint}.*"))
            {
                match = file;
                break;
            }
            if (match is null) return Task.FromResult<MediaCacheHit?>(null);

            var ext = Path.GetExtension(match).TrimStart('.');
            var contentType = EncoderSelector.ContentType(ext);
            var stream = new FileStream(
                match, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<MediaCacheHit?>(new MediaCacheHit(stream, contentType));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media output cache read failed for {Id}/{Fingerprint}", id, fingerprint);
            return Task.FromResult<MediaCacheHit?>(null);
        }
    }

    public async Task SetAsync(string id, string fingerprint, MediaOutput output, CancellationToken ct = default)
    {
        if (!IsSafeKeyPart(id) || !IsSafeKeyPart(fingerprint)) return;

        try
        {
            var dir = ShardDir(id);
            Directory.CreateDirectory(dir);

            var ext = SafeExtension(output.Format);
            var finalPath = Path.Combine(dir, $"{id}-{fingerprint}.{ext}");

            // The key is content-addressed, so an existing entry is identical
            // output — leave it and skip the write.
            if (File.Exists(finalPath)) return;

            // Temp name must NOT share the "{id}-{fingerprint}." prefix the
            // reader globs on, or an in-flight write could be served.
            var tmpPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllBytesAsync(tmpPath, output.Bytes, ct).ConfigureAwait(false);
            try
            {
                File.Move(tmpPath, finalPath, overwrite: false);
            }
            catch (IOException)
            {
                // Another request won the race and wrote the same entry — fine.
                TryDelete(tmpPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media output cache write failed for {Id}/{Fingerprint}", id, fingerprint);
        }
    }

    private string ShardDir(string id)
        => Path.Combine(_root, id.Length >= 2 ? id[..2] : "_");

    private static bool IsSafeKeyPart(string value)
        => !string.IsNullOrEmpty(value)
           && value.Length <= 256
           && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string SafeExtension(string format)
    {
        var slug = format.ToLowerInvariant();
        return EncoderSelector.SupportedFormats.Contains(slug) ? slug : "bin";
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best-effort cleanup of the losing temp file
        }
    }
}
