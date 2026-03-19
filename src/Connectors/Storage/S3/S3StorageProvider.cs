using Koan.Storage.Abstractions;
using Koan.ZenGarden; // IZenGardenClient.BoundEndpoint only
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using MinioItem = Minio.DataModel.Item;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Koan.Storage.Connector.S3;

/// <summary>
/// S3-compatible storage provider using the MinIO .NET SDK.
///
/// Supports standard S3 operations against any S3-compatible endpoint,
/// including Zen Garden's Moss S3 gateway.
///
/// Endpoint resolution: if no explicit endpoint is configured, resolves lazily
/// at first use via the connected Moss's S3 port catalog (ZenGarden.Client.BoundEndpoint).
/// Storage is a first-class garden concept — not an offering.
/// </summary>
public sealed class S3StorageProvider : IStorageProvider, IStatOperations, IServerSideCopy, IListOperations, IPresignOperations, IDisposable
{
    private readonly IOptionsMonitor<S3StorageOptions> _options;
    private readonly ILogger<S3StorageProvider>? _logger;
    private readonly IZenGardenClient? _zenGardenClient;
    private readonly HttpClient _presignHttpClient;
    private IMinioClient? _client;

    public S3StorageProvider(
        IOptionsMonitor<S3StorageOptions> options,
        ILogger<S3StorageProvider>? logger = null,
        IZenGardenClient? zenGardenClient = null)
    {
        _options = options;
        _logger = logger;
        _zenGardenClient = zenGardenClient;
        _presignHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _options.OnChange(_ => RebuildClient());
    }

    public string Name => Infrastructure.S3StorageConstants.ProviderName;

    public StorageProviderCapabilities Capabilities => new(
        SupportsSequentialRead: true,
        SupportsSeek: true,
        SupportsPresignedRead: HasMossEndpoint(),
        SupportsServerSideCopy: true
    );

    public async Task WriteAsync(string container, string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var client = GetClient();
        var bucket = ResolveBucket(container);

        // No EnsureBucketAsync — Moss auto-creates bucket directories on write.
        var args = new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithStreamData(content)
            .WithObjectSize(content.CanSeek ? content.Length - content.Position : -1)
            .WithContentType(contentType ?? "application/octet-stream");

        await client.PutObjectAsync(args, ct);
    }

    public async Task<Stream> OpenReadAsync(string container, string key, CancellationToken ct = default)
    {
        var client = GetClient();
        var bucket = ResolveBucket(container);

        var ms = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithCallbackStream(async (stream, token) =>
            {
                await stream.CopyToAsync(ms, token);
            });

        try
        {
            await client.GetObjectAsync(args, ct);
        }
        catch (Minio.Exceptions.BucketNotFoundException)
        {
            throw new FileNotFoundException($"Container '{container}' does not exist.", key);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            throw new FileNotFoundException($"Object '{key}' not found in container '{container}'.", key);
        }

        ms.Position = 0;
        return ms;
    }

    public async Task<(Stream Stream, long? Length)> OpenReadRangeAsync(string container, string key, long? from, long? to, CancellationToken ct = default)
    {
        var client = GetClient();
        var bucket = ResolveBucket(container);

        // Get total size first for range calculation
        var statArgs = new StatObjectArgs().WithBucket(bucket).WithObject(key);
        var stat = await client.StatObjectAsync(statArgs, ct);

        long start = from ?? 0;
        long end = to.HasValue ? Math.Min(to.Value, stat.Size - 1) : stat.Size - 1;
        long length = end - start + 1;

        var ms = new MemoryStream((int)Math.Min(length, int.MaxValue));
        var args = new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithOffsetAndLength(start, length)
            .WithCallbackStream(async (stream, token) =>
            {
                await stream.CopyToAsync(ms, token);
            });

        await client.GetObjectAsync(args, ct);
        ms.Position = 0;
        return (ms, length);
    }

    public async Task<bool> DeleteAsync(string container, string key, CancellationToken ct = default)
    {
        var client = GetClient();
        var bucket = ResolveBucket(container);

        try
        {
            var args = new RemoveObjectArgs().WithBucket(bucket).WithObject(key);
            await client.RemoveObjectAsync(args, ct);
            return true;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string container, string key, CancellationToken ct = default)
    {
        try
        {
            var stat = await HeadAsync(container, key, ct);
            return stat is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ObjectStat?> HeadAsync(string container, string key, CancellationToken ct = default)
    {
        var client = GetClient();
        var bucket = ResolveBucket(container);

        try
        {
            var args = new StatObjectArgs().WithBucket(bucket).WithObject(key);
            var stat = await client.StatObjectAsync(args, ct);
            return new ObjectStat(
                Length: stat.Size,
                ContentType: stat.ContentType,
                LastModified: stat.LastModified,
                ETag: stat.ETag
            );
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> CopyAsync(string sourceContainer, string sourceKey, string targetContainer, string targetKey, CancellationToken ct = default)
    {
        var client = GetClient();
        var srcBucket = ResolveBucket(sourceContainer);
        var dstBucket = ResolveBucket(targetContainer);

        var src = new CopySourceObjectArgs().WithBucket(srcBucket).WithObject(sourceKey);
        var args = new CopyObjectArgs()
            .WithBucket(dstBucket)
            .WithObject(targetKey)
            .WithCopyObjectSource(src);

        await client.CopyObjectAsync(args, ct);
        return true;
    }

    public async IAsyncEnumerable<StorageObjectInfo> ListObjectsAsync(
        string container,
        string? prefix = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = GetClient();
        var bucket = ResolveBucket(container);

        var args = new ListObjectsArgs()
            .WithBucket(bucket)
            .WithRecursive(true);

        if (!string.IsNullOrEmpty(prefix))
            args = args.WithPrefix(prefix);

        await foreach (var item in ((MinioClient)client).ListObjectsEnumAsync(args, ct).WithCancellation(ct))
        {
            if (item.IsDir) continue;

            yield return new StorageObjectInfo(
                Key: item.Key,
                Size: (long)item.Size,
                LastModified: item.LastModifiedDateTime ?? DateTimeOffset.MinValue,
                ContentType: null,
                ETag: item.ETag
            );
        }
    }

    private IMinioClient GetClient()
    {
        if (_client is not null) return _client;

        // Try explicit config first
        RebuildClient();
        if (_client is not null) return _client;

        // Lazy resolution via ZenGarden: use BoundEndpoint + S3 port catalog
        if (TryResolveViaZenGarden())
        {
            RebuildClient();
            if (_client is not null) return _client;
        }

        throw new InvalidOperationException(
            "S3 endpoint not resolved. Ensure Zen Garden is connected and a storage replica set is available, " +
            "or set Koan:Storage:Providers:S3:Endpoint explicitly.");
    }

    /// <summary>
    /// Lazy two-hop endpoint resolution via the garden storage discovery API.
    ///
    /// 1. GET {bound_moss}/api/v1/garden/storage/{name} → which stone has it (instances)
    /// 2. GET {primary_stone}/api/v1/garden/storage/{name} → s3 block (endpoint, credentials)
    ///
    /// The s3 block is only present when the responding stone hosts the storage locally.
    /// The bound Moss acts as directory; the primary stone answers for itself.
    /// </summary>
    private bool TryResolveViaZenGarden()
    {
        var mossEndpoint = _zenGardenClient?.BoundEndpoint;
        if (string.IsNullOrWhiteSpace(mossEndpoint))
            return false;

        try
        {
            var replicaSet = _options.CurrentValue.ReplicaSet ?? "storage";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var path = $"/api/v1/garden/storage/{Uri.EscapeDataString(replicaSet)}";

            // Hop 1: Ask bound Moss "who has this replica set?"
            var hop1 = QueryStorageDiscovery(http, mossEndpoint, path);
            if (hop1 is null) return false;

            // If the bound Moss has the s3 block, we're done (storage is local to bound Moss)
            if (TryExtractS3(hop1.Value, mossEndpoint))
                return true;

            // Find primary stone endpoint from instances
            var primaryEndpoint = FindPrimaryEndpoint(hop1.Value);
            if (primaryEndpoint is null || primaryEndpoint == mossEndpoint.TrimEnd('/'))
            {
                _logger?.LogDebug("No reachable primary for replica set '{ReplicaSet}'", replicaSet);
                return false;
            }

            // Hop 2: Ask the primary stone directly — it has the s3 block
            var hop2 = QueryStorageDiscovery(http, primaryEndpoint, path);
            if (hop2 is null) return false;

            return TryExtractS3(hop2.Value, primaryEndpoint);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Garden storage resolution failed");
            return false;
        }
    }

    private static JsonElement? QueryStorageDiscovery(HttpClient http, string endpoint, string path)
    {
        var url = $"{endpoint.TrimEnd('/')}{path}";
        var response = http.GetAsync(url).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode) return null;

        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return root.TryGetProperty("data", out var data) ? data : root;
    }

    private bool TryExtractS3(JsonElement data, string stoneEndpoint)
    {
        if (!data.TryGetProperty("s3", out var s3))
            return false;

        var s3Endpoint = s3.TryGetProperty("endpoint", out var ep) ? ep.GetString() : null;
        var accessKey = s3.TryGetProperty("access_key", out var ak) ? ak.GetString() : null;
        var secretKey = s3.TryGetProperty("secret_key", out var sk) ? sk.GetString() : null;

        if (string.IsNullOrWhiteSpace(s3Endpoint))
            return false;

        var opts = _options.CurrentValue;
        opts.Endpoint = s3Endpoint;
        opts.MossEndpoint = stoneEndpoint;
        if (!string.IsNullOrWhiteSpace(accessKey)) opts.AccessKey = accessKey;
        if (!string.IsNullOrWhiteSpace(secretKey)) opts.SecretKey = secretKey;

        _logger?.LogInformation("S3 resolved: {Endpoint}", s3Endpoint);
        return true;
    }

    private static string? FindPrimaryEndpoint(JsonElement data)
    {
        if (!data.TryGetProperty("instances", out var instances))
            return null;

        string? firstEndpoint = null;
        foreach (var instance in instances.EnumerateArray())
        {
            var ep = instance.TryGetProperty("endpoint", out var epProp) ? epProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(ep)) continue;

            firstEndpoint ??= ep;

            var role = instance.TryGetProperty("role", out var rp) ? rp.GetString() : null;
            if (string.Equals(role, "primary", StringComparison.OrdinalIgnoreCase))
                return ep;
        }
        return firstEndpoint;
    }

    private void RebuildClient()
    {
        var opts = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(opts.Endpoint)) return;

        var builder = new MinioClient()
            .WithEndpoint(opts.Endpoint);

        // Credentials come from garden storage discovery (generated per replica set)
        // or from explicit configuration. Always present after resolution.
        if (!string.IsNullOrWhiteSpace(opts.AccessKey) && !string.IsNullOrWhiteSpace(opts.SecretKey))
        {
            builder = builder.WithCredentials(opts.AccessKey, opts.SecretKey);
        }

        if (opts.UseSsl)
        {
            builder = builder.WithSSL();
        }

        builder = builder.WithRegion(opts.Region);

        _client = builder.Build();
    }

    private string ResolveBucket(string container)
    {
        var prefix = _options.CurrentValue.BucketPrefix;

        // Lazy resolve: if configurator ran before AppHost.Identity was populated,
        // BucketPrefix may be the default. Re-check at request time.
        if (string.IsNullOrWhiteSpace(prefix))
        {
            var appCode = Koan.Core.Hosting.App.AppHost.Identity.Code;
            if (!string.IsNullOrWhiteSpace(appCode) && appCode != "koan-app")
            {
                prefix = appCode;
                _options.CurrentValue.BucketPrefix = prefix;
            }
        }

        if (string.IsNullOrWhiteSpace(prefix))
            return container;
        return $"{prefix}-{container}";
    }

    public async Task<Uri> PresignReadAsync(string container, string key, TimeSpan expiry, CancellationToken ct = default)
    {
        return await PresignViaMossAsync(container, key, "GET", expiry, null, ct);
    }

    public async Task<Uri> PresignWriteAsync(string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
    {
        return await PresignViaMossAsync(container, key, "PUT", expiry, contentType, ct);
    }

    /// <summary>
    /// Sends a presign request to the Moss-native presign endpoint.
    /// POST {MossEndpoint}/api/v1/storage/s3/presign
    /// </summary>
    private async Task<Uri> PresignViaMossAsync(
        string container, string key, string method, TimeSpan expiry,
        string? contentType, CancellationToken ct)
    {
        var mossEndpoint = _options.CurrentValue.MossEndpoint;
        if (string.IsNullOrWhiteSpace(mossEndpoint))
        {
            throw new NotSupportedException(
                "Presigned URLs require a Moss HTTP endpoint. Set S3StorageOptions.MossEndpoint or use Zen Garden discovery.");
        }

        var bucket = ResolveBucket(container);
        var url = $"{mossEndpoint.TrimEnd('/')}/api/v1/storage/s3/presign";

        var payload = new
        {
            bucket,
            key,
            method,
            expiry_seconds = (int)expiry.TotalSeconds,
            content_type = contentType
        };

        var response = await _presignHttpClient.PostAsJsonAsync(url, payload, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        // Expect response: { "url": "..." } or { "data": { "url": "..." } }
        var root = doc.RootElement;
        string? presignedUrl = null;

        if (root.TryGetProperty("url", out var urlProp))
        {
            presignedUrl = urlProp.GetString();
        }
        else if (root.TryGetProperty("data", out var dataProp) && dataProp.TryGetProperty("url", out var dataUrlProp))
        {
            presignedUrl = dataUrlProp.GetString();
        }

        if (string.IsNullOrWhiteSpace(presignedUrl))
        {
            throw new InvalidOperationException("Moss presign endpoint returned no URL.");
        }

        _logger?.LogDebug("Presigned {Method} URL generated for {Bucket}/{Key}", method, bucket, key);
        return new Uri(presignedUrl);
    }

    private bool HasMossEndpoint()
    {
        return !string.IsNullOrWhiteSpace(_options.CurrentValue.MossEndpoint);
    }

    /// <summary>
    /// Swaps the internal MinIO client to point at a new endpoint.
    /// Called by <c>GardenAwareEndpointManager</c> on endpoint change.
    /// </summary>
    internal void SwapClient(IMinioClient newClient)
    {
        var old = _client;
        _client = newClient;
        (old as IDisposable)?.Dispose();
    }

    public void Dispose()
    {
        (_client as IDisposable)?.Dispose();
        _presignHttpClient.Dispose();
    }
}
