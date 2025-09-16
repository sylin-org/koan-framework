using Koan.Storage.Abstractions;

namespace Koan.Storage.Extensions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Koan.Storage;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Text;
// using System.Text.Json; // replaced by Newtonsoft.Json per framework policy

public static class StorageServiceHelpers
{
    // Orchestrated transfer convenience
    public static Task<StorageObject> TransferToProfile(this IStorageService svc, string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default)
        => svc.TransferToProfileAsync(sourceProfile, sourceContainer, key, targetProfile, targetContainer, deleteSource, ct);

    public static Task<StorageObject> CopyTo(this IStorageService svc, string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, CancellationToken ct = default)
        => svc.TransferToProfileAsync(sourceProfile, sourceContainer, key, targetProfile, targetContainer, deleteSource: false, ct);

    public static Task<StorageObject> MoveTo(this IStorageService svc, string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, CancellationToken ct = default)
        => svc.TransferToProfileAsync(sourceProfile, sourceContainer, key, targetProfile, targetContainer, deleteSource: true, ct);

    // Write / Onboard

    public static Task<StorageObject> CreateTextFile(this IStorageService svc, string key, string content, string? contentType = "text/plain; charset=utf-8", string profile = "", string container = "", CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return svc.Create(key, bytes, contentType, profile, container, ct);
    }

    public static Task<StorageObject> CreateJson<T>(this IStorageService svc, string key, T value, JsonSerializerSettings? options = null, string profile = "", string container = "", string contentType = "application/json; charset=utf-8", CancellationToken ct = default)
    {
        // Serialize any value (object, array, primitive) using Newtonsoft.Json for broad compatibility
        var json = JsonConvert.SerializeObject(value, options ?? new JsonSerializerSettings());
        return svc.CreateJson(key, json, profile, container, contentType, ct);
    }

    public static Task<StorageObject> Create(this IStorageService svc, string key, ReadOnlyMemory<byte> bytes, string? contentType = "application/octet-stream", string profile = "", string container = "", CancellationToken ct = default)
    {
        // Wrap bytes in a non-owning stream if possible; MemoryStream requires array, so copy if not array-backed
        byte[] array = bytes.ToArray();
        using var ms = new MemoryStream(array, 0, array.Length, writable: false, publiclyVisible: true);
        return svc.Onboard(key, ms, contentType, profile, container, ct);
    }

    public static Task<StorageObject> Onboard(this IStorageService svc, string key, Stream content, string? contentType = null, string profile = "", string container = "", CancellationToken ct = default)
    {
        contentType ??= GuessContentTypeInternal(key) ?? "application/octet-stream";
        return svc.PutAsync(profile, container, key.ToStorageKey(), content, contentType, ct);
    }

    // JSON helpers
    public static Task<StorageObject> Create(this IStorageService svc, string key, JObject obj, string contentType = "application/json; charset=utf-8", string profile = "", string container = "", CancellationToken ct = default)
    {
        // Serialize without indentation to keep payload compact; JObject is already the shape source
        var json = obj.ToString(Formatting.None);
        return svc.CreateTextFile(key, json, contentType, profile, container, ct);
    }

    public static Task<StorageObject> CreateJson(this IStorageService svc, string key, string json, string profile = "", string container = "", string contentType = "application/json; charset=utf-8", CancellationToken ct = default)
        => svc.CreateTextFile(key, json, contentType, profile, container, ct);

    public static async Task<StorageObject> OnboardFile(this IStorageService svc, string filePath, string? key = null, string? contentType = null, string profile = "", string container = "", CancellationToken ct = default)
    {
        key ??= Path.GetFileName(filePath);
        contentType ??= GuessContentTypeInternal(filePath) ?? "application/octet-stream";
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await svc.PutAsync(profile, container, key.ToStorageKey(), fs, contentType, ct).ConfigureAwait(false);
    }

    public static async Task<StorageObject> OnboardUrl(this IStorageService svc, Uri uri, string? key = null, string? contentType = null, HttpClient? http = null, string profile = "", string container = "", long? maxBytes = null, CancellationToken ct = default)
    {
        key ??= Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(key)) key = Guid.CreateVersion7().ToString("n");

        var ownClient = http is null;
        http ??= new HttpClient();
        try
        {
            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var limited = maxBytes.HasValue ? new LimitedStream(stream, maxBytes.Value) : stream;
            contentType ??= resp.Content.Headers.ContentType?.ToString() ?? GuessContentTypeInternal(key!) ?? "application/octet-stream";
            return await svc.PutAsync(profile, container, key!.ToStorageKey(), limited, contentType, ct).ConfigureAwait(false);
        }
        finally
        {
            if (ownClient)
            {
                http.Dispose();
            }
        }
    }

    // Read helpers
    public static async Task<string> ReadAllText(this IStorageService svc, string profile, string container, string key, Encoding? encoding = null, CancellationToken ct = default)
    {
        encoding ??= Encoding.UTF8;
        await using var s = await svc.ReadAsync(profile, container, key, ct).ConfigureAwait(false);
        using var sr = new StreamReader(s, encoding, detectEncodingFromByteOrderMarks: true);
        return await sr.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadAllBytes(this IStorageService svc, string profile, string container, string key, CancellationToken ct = default)
    {
        await using var s = await svc.ReadAsync(profile, container, key, ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    public static async Task<string> ReadRangeAsString(this IStorageService svc, string profile, string container, string key, long from, long to, Encoding? encoding = null, CancellationToken ct = default)
    {
        encoding ??= Encoding.UTF8;
        var (stream, _) = await svc.ReadRangeAsync(profile, container, key, from, to, ct).ConfigureAwait(false);
        using var sr = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        return await sr.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    // Lifecycle utilities
    public static Task<bool> TryDelete(this IStorageService svc, string profile, string container, string key, CancellationToken ct = default)
        => svc.DeleteAsync(profile, container, key, ct);

    public static async Task EnsureDeleted(this IStorageService svc, string profile, string container, string key, CancellationToken ct = default)
    {
        _ = await svc.DeleteAsync(profile, container, key, ct).ConfigureAwait(false);
    }

    // Routing sugar
    public static ProfiledStorage InProfile(this IStorageService svc, string profile, string? container = null)
        => new(svc, profile, container ?? "");

    // Key and content-type helpers
    public static string ToStorageKey(this string input)
    {
        var normalized = input.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        return normalized.TrimStart('/');
    }

    public static string? GuessContentType(this string fileNameOrKey)
        => GuessContentTypeInternal(fileNameOrKey);

    private static string? GuessContentTypeInternal(string fileNameOrKey)
    {
        var ext = Path.GetExtension(fileNameOrKey);
        if (string.IsNullOrWhiteSpace(ext)) return null;
        ext = ext.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "txt" => "text/plain; charset=utf-8",
            "md" => "text/markdown; charset=utf-8",
            "json" => "application/json; charset=utf-8",
            "csv" => "text/csv; charset=utf-8",
            "xml" => "application/xml",
            "html" => "text/html; charset=utf-8",
            "htm" => "text/html; charset=utf-8",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            "pdf" => "application/pdf",
            "zip" => "application/zip",
            _ => null
        };
    }

    private sealed class LimitedStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;
        public LimitedStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _remaining = maxBytes;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) throw new IOException("Maximum allowed bytes exceeded.");
            var toRead = (int)Math.Min(count, _remaining);
            var read = _inner.Read(buffer, offset, toRead);
            _remaining -= read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0) throw new IOException("Maximum allowed bytes exceeded.");
            var toRead = (int)Math.Min(buffer.Length, _remaining);
            var read = await _inner.ReadAsync(buffer.Slice(0, toRead), cancellationToken).ConfigureAwait(false);
            _remaining -= read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}