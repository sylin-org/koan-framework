using System.Collections.Concurrent;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Couchbase;
using Couchbase.Core.IO.Authentication.Authenticators;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Koan.Core;
using Koan.Core.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Couchbase;

internal sealed class CouchbaseCollectionContext
{
    public CouchbaseCollectionContext(ICluster cluster, IBucket bucket, IScope scope, ICouchbaseCollection collection, string bucketName, string scopeName, string collectionName)
    {
        Cluster = cluster;
        Bucket = bucket;
        Scope = scope;
        Collection = collection;
        BucketName = bucketName;
        ScopeName = scopeName;
        CollectionName = collectionName;
    }

    public ICluster Cluster { get; }
    public IBucket Bucket { get; }
    public IScope Scope { get; }
    public ICouchbaseCollection Collection { get; }
    public string BucketName { get; }
    public string ScopeName { get; }
    public string CollectionName { get; }
}

internal sealed class CouchbaseClusterProvider : IAsyncDisposable, IAdapterReadiness, IAsyncAdapterInitializer
{
    private readonly IOptionsMonitor<CouchbaseOptions> _options;
    private readonly ILogger<CouchbaseClusterProvider>? _logger;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly object _initializationGate = new();
    private readonly HttpClient _httpClient;
    private ICluster? _cluster;
    private IBucket? _bucket;
    private string? _bucketName;
    private readonly ConcurrentDictionary<string, IScope> _scopes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ICouchbaseCollection> _collections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReadinessStateManager _stateManager = new();
    private Task? _initialization;

    public CouchbaseClusterProvider(IOptionsMonitor<CouchbaseOptions> options, ILogger<CouchbaseClusterProvider>? logger = null)
    {
        _options = options;
        _logger = logger;
        _httpClient = new HttpClient();
        _stateManager.StateChanged += (_, args) => ReadinessStateChanged?.Invoke(this, args);
    }

    public AdapterReadinessState ReadinessState => _stateManager.State;

    public bool IsReady => _stateManager.IsReady;

    public TimeSpan ReadinessTimeout
    {
        get
        {
            var timeout = _options.CurrentValue.Readiness.Timeout;
            return timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(30);
        }
    }

    public event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged;

    public ReadinessStateManager StateManager => _stateManager;

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        await EnsureInitialized(ct).ConfigureAwait(false);
        return _stateManager.IsReady;
    }

    public async Task WaitForReadiness(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        await EnsureInitialized(ct).ConfigureAwait(false);

        if (_stateManager.IsReady)
        {
            return;
        }

        if (_stateManager.State == AdapterReadinessState.Failed)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State, "Couchbase adapter failed to initialize.");
        }

        var effectiveTimeout = timeout ?? ReadinessTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State,
                "Couchbase readiness timeout is zero; gating cannot wait for readiness.");
        }

        try
        {
            await _stateManager.Wait(effectiveTimeout, ct);
        }
        catch (TimeoutException ex)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State,
                $"Timed out waiting for Couchbase readiness after {effectiveTimeout}.", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (_stateManager.State == AdapterReadinessState.Failed)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State,
                "Couchbase adapter failed while waiting for readiness.", ex);
        }

        if (!_stateManager.IsReady)
        {
            throw new AdapterNotReadyException(GetType().Name, _stateManager.State,
                "Couchbase adapter is not ready after waiting for readiness.");
        }
    }

    private static (string Username, string Password) GenerateDefaultCredentials()
    {
        var projectName = Assembly.GetEntryAssembly()?.GetName().Name ?? "KoanApplication";
        var passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(projectName));
        var password = Convert.ToHexString(passwordHash).ToLowerInvariant();
        return ("KoanUser", password);
    }

    private async Task<bool> IsClusterInitialized(string baseUrl, string username, string password, CancellationToken ct)
    {
        try
        {
            // Use a fresh HttpClient instance to avoid auth header contamination
            using var checkClient = new HttpClient();

            // Try with authentication - only properly initialized clusters accept specific credentials
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            checkClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            var response = await checkClient.GetAsync($"{baseUrl}/pools/default", ct);

            // If 401 Unauthorized, cluster exists but our credentials are wrong (uninitialized or different creds)
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return false;
            }

            // If successful with our credentials, verify we can actually access admin functions
            if (response.IsSuccessStatusCode)
            {
                // Test if we can access the buckets endpoint - only works if properly initialized
                var bucketResponse = await checkClient.GetAsync($"{baseUrl}/pools/default/buckets", ct);
                return bucketResponse.IsSuccessStatusCode;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task InitializeCluster(string baseUrl, string username, string password, CancellationToken ct)
    {
        _logger?.LogDebug("Initializing Couchbase cluster at {BaseUrl} with user {Username}", baseUrl, username);

        // For fresh cluster initialization, create the initial admin user
        var initData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password),
            new KeyValuePair<string, string>("port", "8091"),
            new KeyValuePair<string, string>("services", "kv,n1ql,index,fts"),
            new KeyValuePair<string, string>("memoryQuota", "512"),
            new KeyValuePair<string, string>("indexMemoryQuota", "256"),
            new KeyValuePair<string, string>("ftsMemoryQuota", "256"),
            new KeyValuePair<string, string>("clusterName", "koan-cluster"),
            new KeyValuePair<string, string>("sendStats", "false")
        });

        // Use a fresh HttpClient instance for cluster initialization to avoid auth header contamination
        using var initClient = new HttpClient();
        var response = await initClient.PostAsync($"{baseUrl}/clusterInit", initData, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger?.LogWarning("Cluster initialization failed with status {StatusCode}: {Error}", response.StatusCode, error);

            // If cluster already exists, this is actually success
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                (error.Contains("Cluster is already initialized") || error.Contains("already initialized")))
            {
                _logger?.LogDebug("Couchbase cluster is already initialized");
                return;
            }

            throw new InvalidOperationException($"Failed to initialize Couchbase cluster (HTTP {response.StatusCode}): {error}");
        }

        _logger?.LogInformation("Couchbase cluster initialized successfully");
    }

    private async Task<bool> WaitForCouchbaseAndCheckInitialization(string baseUrl, string username, string password, string bucketName, CancellationToken ct)
    {
        const int maxRetries = 12; // Wait up to 60 seconds (5s * 12)
        const int delayMs = 5000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // .NET HttpClient on net10 reports "response ended prematurely" against
                // Couchbase's management responses when reading the full body — Couchbase sends a
                // Content-Length that doesn't match what comes over the socket on some paths.
                // ResponseHeadersRead lets us treat headers-arrived as readiness.
                using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await healthClient.GetAsync($"{baseUrl}/ui/index.html", HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("Couchbase web console is ready");

                    // Check if cluster is already initialized
                    var alreadyInitialized = await IsClusterInitialized(baseUrl, username, password, ct);
                    if (!alreadyInitialized)
                    {
                        // Initialize cluster
                        await InitializeCluster(baseUrl, username, password, ct);

                        // Ensure bucket exists
                        await EnsureBucketExists(baseUrl, username, password, bucketName, ct);
                    }
                    else
                    {
                        _logger?.LogDebug("Couchbase cluster is already initialized");
                    }

                    // Indexer storage mode must be set before any CREATE INDEX (including the
                    // primary indexes Koan creates per collection). Idempotent — POSTs the same
                    // value whether the cluster was fresh or pre-initialized (e.g. Testcontainers).
                    await EnsureIndexerStorageMode(baseUrl, username, password, ct);

                    // Wait for N1QL service to become query-ready
                    await WaitForN1QLServiceReadiness(baseUrl, username, password, ct);

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Waiting for Couchbase to be ready (attempt {Attempt}/{MaxAttempts})", i + 1, maxRetries);
            }

            if (i < maxRetries - 1) // Don't wait on the last iteration
            {
                await Task.Delay(delayMs, ct);
            }
        }

        _logger?.LogWarning("Couchbase was not ready after {MaxRetries} attempts. Proceeding without initialization.", maxRetries);
        _stateManager.TransitionTo(AdapterReadinessState.Degraded);
        return false;
    }

    private async Task EnsureIndexerStorageMode(string baseUrl, string username, string password, CancellationToken ct)
    {
        try
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            // Try plasma first (Enterprise default), fall back to forestdb (Community editions
            // reject plasma with HTTP 400 — "The value must be one of the following: [forestdb]").
            foreach (var mode in new[] { "plasma", "forestdb" })
            {
                var body = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("storageMode", mode)
                });
                var resp = await client.PostAsync($"{baseUrl}/settings/indexes", body, ct);
                if (resp.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("Indexer storage mode set to {Mode}", mode);
                    return;
                }
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger?.LogDebug("Indexer storage mode={Mode} POST returned {Status}: {Error}", mode, resp.StatusCode, err);
            }
            _logger?.LogWarning("Failed to set indexer storage mode; CREATE PRIMARY INDEX may fail");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not set indexer storage mode");
        }
    }

    private async Task EnsureBucketExists(string baseUrl, string username, string password, string bucketName, CancellationToken ct)
    {
        try
        {
            // Check if bucket exists
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            var response = await _httpClient.GetAsync($"{baseUrl}/pools/default/buckets/{bucketName}", ct);

            if (response.IsSuccessStatusCode)
            {
                _logger?.LogDebug("Bucket {BucketName} already exists", bucketName);
                return;
            }

            // Create bucket if it doesn't exist
            _logger?.LogDebug("Creating bucket {BucketName}", bucketName);

            var bucketData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("name", bucketName),
                new KeyValuePair<string, string>("bucketType", "couchbase"),
                new KeyValuePair<string, string>("ramQuotaMB", "256"),
                new KeyValuePair<string, string>("replicaNumber", "0"),
                new KeyValuePair<string, string>("flushEnabled", "0")
            });

            var bucketResponse = await _httpClient.PostAsync($"{baseUrl}/pools/default/buckets", bucketData, ct);

            if (!bucketResponse.IsSuccessStatusCode)
            {
                var error = await bucketResponse.Content.ReadAsStringAsync(ct);
                _logger?.LogWarning("Bucket creation failed with status {StatusCode}: {Error}", bucketResponse.StatusCode, error);

                // If bucket already exists, this is success
                if (bucketResponse.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                    (error.Contains("already exists") || error.Contains("Bucket with given name already exists")))
                {
                    _logger?.LogDebug("Bucket {BucketName} already exists", bucketName);
                }
                // Don't throw - bucket might exist or be created later
            }
            else
            {
                _logger?.LogInformation("Bucket {BucketName} created successfully", bucketName);

                // Wait for bucket to be fully ready before continuing
                await Task.Delay(2000, ct);

                // Verify bucket is accessible
                for (int i = 0; i < 5; i++)
                {
                    var verifyResponse = await _httpClient.GetAsync($"{baseUrl}/pools/default/buckets/{bucketName}", ct);
                    if (verifyResponse.IsSuccessStatusCode)
                    {
                        _logger?.LogDebug("Bucket {BucketName} verified accessible", bucketName);
                        break;
                    }
                    if (i < 4) await Task.Delay(1000, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error checking/creating bucket {BucketName}", bucketName);
            // Don't throw - continue with cluster connection
            _stateManager.TransitionTo(AdapterReadinessState.Degraded);
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async ValueTask<CouchbaseCollectionContext> GetCollectionContext(string collectionName, CancellationToken ct)
    {
        await EnsureInitialized(ct).ConfigureAwait(false);
        return await GetCollectionContextCore(collectionName, ct).ConfigureAwait(false);
    }

    private async ValueTask<CouchbaseCollectionContext> GetCollectionContextCore(string collectionName, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var cluster = await EnsureCluster(options, ct);
        var bucket = await EnsureBucket(cluster, options, ct);

        // Partition routing: EntityContext.Current.Partition wins over CouchbaseOptions.Scope.
        // CouchbaseAdapterFactory.ResolveStorage returns just the collection (no partition suffix);
        // we map the partition onto the scope, which is Couchbase's native isolation primitive.
        var partition = Koan.Data.Core.EntityContext.Current?.Partition;
        string scopeName;
        if (!string.IsNullOrWhiteSpace(partition))
        {
            scopeName = SanitizeIdentifier(partition);
        }
        else
        {
            scopeName = string.IsNullOrWhiteSpace(options.Scope) ? "_default" : options.Scope!;
        }

        var scope = await GetScope(bucket, scopeName);
        var finalCollection = string.IsNullOrWhiteSpace(collectionName)
            ? (!string.IsNullOrWhiteSpace(options.Collection) ? options.Collection! : "_default")
            : collectionName;
        var collection = await GetCollection(scope, scopeName, finalCollection);
        return new CouchbaseCollectionContext(cluster, bucket, scope, collection, bucket.Name, scopeName, finalCollection);
    }

    // Scope identifier rules (sanitization + 30-byte bounding) live in one place.
    private static string SanitizeIdentifier(string value) => CouchbaseAdapterFactory.FormatScope(value);

    private async ValueTask<ICluster> EnsureCluster(CouchbaseOptions options, CancellationToken ct)
    {
        if (_cluster is not null)
        {
            return _cluster;
        }

        await _sync.WaitAsync(ct);
        try
        {
            if (_cluster is null)
            {
                string username, password;

                if (!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password))
                {
                    username = options.Username;
                    password = options.Password;
                    _logger?.LogDebug("Using configured Couchbase credentials");
                }
                else
                {
                    (username, password) = GenerateDefaultCredentials();
                    _logger?.LogDebug("Auto-provisioned default Couchbase credentials for user {Username}", username);
                }

                // Resolve the management URL. Explicit CouchbaseOptions.ManagementUrl wins —
                // necessary for test/container environments where KV and management are mapped
                // to independent host ports. Otherwise derive from the KV connection string by
                // swapping the scheme and defaulting to port 8091.
                string baseUrl;
                if (!string.IsNullOrWhiteSpace(options.ManagementUrl))
                {
                    baseUrl = options.ManagementUrl!.TrimEnd('/');
                }
                else
                {
                    baseUrl = options.ConnectionString.Replace("couchbase://", "http://").TrimEnd('/');
                    var schemeEnd = baseUrl.IndexOf("://") + 3;
                    if (baseUrl.IndexOf(':', schemeEnd) == -1)
                    {
                        baseUrl += ":8091";
                    }
                }

                // When ManagementUrl is explicitly configured we assume the caller (e.g.
                // Testcontainers) has already waited for the cluster to come up — the .NET 10
                // HttpClient's stricter response parsing reports "response ended prematurely"
                // against Couchbase's management endpoints and makes the probe loop unreliable.
                // We still need to ensure the indexer storage mode is set (Testcontainers Couchbase
                // doesn't always set it) and the N1QL service is reachable.
                if (!string.IsNullOrWhiteSpace(options.ManagementUrl))
                {
                    await EnsureIndexerStorageMode(baseUrl, username, password, ct);
                    await WaitForN1QLServiceReadiness(baseUrl, username, password, ct);
                }
                else if (!await WaitForCouchbaseAndCheckInitialization(baseUrl, username, password, options.Bucket, ct))
                {
                    _logger?.LogDebug("Couchbase cluster initialization was not needed or failed gracefully");
                }

                var clusterOptions = new global::Couchbase.ClusterOptions();
                clusterOptions.WithAuthenticator(new PasswordAuthenticator(
                    username,
                    password,
                    options.ConnectionString.StartsWith("couchbases://", StringComparison.OrdinalIgnoreCase)));

                _logger?.LogDebug("Connecting to Couchbase cluster at {ConnectionString}", Redaction.DeIdentify(options.ConnectionString));
                _cluster = await Cluster.ConnectAsync(options.ConnectionString, clusterOptions);

                _logger?.LogDebug("Waiting for Couchbase cluster to finish bootstrapping");
                await _cluster.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));
                _logger?.LogDebug("Couchbase cluster bootstrap completed");
            }
        }
        finally
        {
            _sync.Release();
        }

        return _cluster!;
    }

    private async ValueTask<IBucket> EnsureBucket(ICluster cluster, CouchbaseOptions options, CancellationToken ct)
    {
        if (_bucket is not null && string.Equals(_bucketName, options.Bucket, StringComparison.OrdinalIgnoreCase))
        {
            return _bucket;
        }

        await _sync.WaitAsync(ct);
        try
        {
            if (_bucket is null || !string.Equals(_bucketName, options.Bucket, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Opening Couchbase bucket {Bucket}", options.Bucket);

                // Retry bucket access with bounded exponential backoff for connection issues.
                // The Couchbase server SDK sometimes throws MultiplexingConnection /
                // SocketNotAvailable during the container's internal bucket warm-up even after the
                // management API has reported the cluster healthy. The previous 5-attempt /
                // 500ms-base schedule only waited ~7.5s before giving up, well short of the
                // ~30s a fresh Testcontainers Couchbase bucket needs.
                const int maxRetries = 12;
                const int baseDelayMs = 500;
                const int maxDelayMs = 3000;

                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        _bucket = await cluster.BucketAsync(options.Bucket);

                        // Wait for bucket to be ready before proceeding
                        await _bucket.WaitUntilReadyAsync(TimeSpan.FromSeconds(10));

                        _bucketName = options.Bucket;
                        _scopes.Clear();
                        _collections.Clear();

                        if (attempt > 0)
                        {
                            _logger?.LogInformation("Successfully opened Couchbase bucket {Bucket} after {Attempt} attempts", options.Bucket, attempt + 1);
                        }
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries - 1 &&
                        (ex.Message.Contains("MultiplexingConnection") || ex.Message.Contains("SocketNotAvailable")))
                    {
                        var delay = Math.Min(maxDelayMs, baseDelayMs * (int)Math.Pow(2, attempt));
                        _logger?.LogDebug(ex, "Bucket access attempt {Attempt}/{MaxRetries} failed, retrying in {Delay}ms", attempt + 1, maxRetries, delay);
                        await Task.Delay(delay, ct);
                    }
                }
            }
        }
        finally
        {
            _sync.Release();
        }

        return _bucket!;
    }

    private async ValueTask<IScope> GetScope(IBucket bucket, string scopeName)
    {
        if (_scopes.TryGetValue(scopeName, out var cached))
        {
            return cached;
        }

        IScope scope;
        if (string.Equals(scopeName, "_default", StringComparison.Ordinal))
        {
            scope = bucket.Scope("_default");
        }
        else
        {
            scope = await bucket.ScopeAsync(scopeName);
        }

        _scopes[scopeName] = scope;
        return scope;
    }

    private async ValueTask<ICouchbaseCollection> GetCollection(IScope scope, string scopeName, string collectionName)
    {
        var key = $"{scopeName}:{collectionName}";
        if (_collections.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ICouchbaseCollection collection;
        if (string.Equals(collectionName, "_default", StringComparison.Ordinal))
        {
            collection = scope.Collection("_default");
        }
        else
        {
            try
            {
                collection = await scope.CollectionAsync(collectionName);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Collection {CollectionName} not found, attempting to create it", collectionName);

                // Try to create the collection if it doesn't exist
                await EnsureCollectionExists(scope, scopeName, collectionName);

                // Now try to get it again
                collection = await scope.CollectionAsync(collectionName);
            }
        }

        _collections[key] = collection;
        return collection;
    }

    private async Task EnsureCollectionExists(IScope scope, string scopeName, string collectionName)
    {
        try
        {
            // Get the collection manager from the bucket (not scope)
            var bucket = scope.Bucket;
            var collectionManager = bucket.Collections;

            // Create scope if it's not the default one
            if (!string.Equals(scopeName, "_default", StringComparison.Ordinal))
            {
                try
                {
                    await collectionManager.CreateScopeAsync(scopeName);
                }
                catch (global::Couchbase.CouchbaseException ex) when (IsAlreadyExists(ex))
                {
                    // Scope already exists, continue
                }
            }

            // Create the collection
            var settings = new CreateCollectionSettings();
            await collectionManager.CreateCollectionAsync(scopeName, collectionName, settings);
            _logger?.LogInformation("Created Couchbase collection {ScopeName}.{CollectionName}", scopeName, collectionName);
        }
        catch (global::Couchbase.CouchbaseException ex) when (IsAlreadyExists(ex))
        {
            // Collection already exists - this is fine
            _logger?.LogDebug("Collection {ScopeName}.{CollectionName} already exists", scopeName, collectionName);
        }
        catch (Exception ex)
        {
            // Other creation failures - log but don't throw
            _logger?.LogWarning(ex, "Failed to create collection {ScopeName}.{CollectionName}: {Error}", scopeName, collectionName, ex.Message);
            _stateManager.TransitionTo(AdapterReadinessState.Degraded);
        }
    }

    private static bool IsAlreadyExists(global::Couchbase.CouchbaseException ex)
    {
        return ex.Context?.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task WaitForN1QLServiceReadiness(string baseUrl, string username, string password, CancellationToken ct)
    {
        const int maxRetries = 10; // Wait up to 30 seconds (3s * 10)
        const int delayMs = 3000;

        _logger?.LogDebug("Waiting for N1QL query service to become ready");

        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var queryClient = new HttpClient();
                queryClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

                // Execute a simple N1QL query to verify service readiness
                var queryData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("statement", "SELECT 1 AS test")
                });

                var response = await queryClient.PostAsync($"{baseUrl}/query/service", queryData, ct);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(ct);
                    if (responseContent.Contains("\"test\":1") || responseContent.Contains("\"status\":\"success\""))
                    {
                        _logger?.LogInformation("N1QL query service is ready and responding");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Waiting for N1QL service readiness (attempt {Attempt}/{MaxAttempts})", i + 1, maxRetries);
            }

            if (i < maxRetries - 1) // Don't wait on the last iteration
            {
                await Task.Delay(delayMs, ct);
            }
        }

        _logger?.LogWarning("N1QL service was not ready after {MaxRetries} attempts. Service may still be initializing.", maxRetries);
        // Don't fail - transition to degraded state and let the application handle N1QL unavailability
        _stateManager.TransitionTo(AdapterReadinessState.Degraded);
    }

    public Task InitializeAsync(CancellationToken ct = default) => EnsureInitialized(ct);

    internal async Task Probe(CancellationToken ct)
    {
        await EnsureInitialized(ct).ConfigureAwait(false);
        await _cluster!.PingAsync(new global::Couchbase.Diagnostics.PingOptions().CancellationToken(ct)).ConfigureAwait(false);
    }

    private Task EnsureInitialized(CancellationToken ct)
    {
        if (_stateManager.IsReady)
        {
            return Task.CompletedTask;
        }

        Task initialization;
        lock (_initializationGate)
        {
            _initialization ??= InitializeCoreAsync(CancellationToken.None);
            initialization = _initialization;
        }

        return initialization.WaitAsync(ct);
    }

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        _stateManager.TransitionTo(AdapterReadinessState.Initializing);

        try
        {
            var options = _options.CurrentValue;
            var cluster = await EnsureCluster(options, ct).ConfigureAwait(false);
            _ = await EnsureBucket(cluster, options, ct).ConfigureAwait(false);

            if (_stateManager.State == AdapterReadinessState.Degraded)
            {
                _logger?.LogWarning("Couchbase adapter entering degraded readiness due to partial initialization.");
            }
            else
            {
                _stateManager.TransitionTo(AdapterReadinessState.Ready);
            }
        }
        catch (Exception ex)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Failed);
            _logger?.LogError(ex, "Failed to initialize Couchbase adapter");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cluster is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _cluster?.Dispose();
        }

        _httpClient?.Dispose();
        _sync?.Dispose();
        _initialization = null;
    }
}

