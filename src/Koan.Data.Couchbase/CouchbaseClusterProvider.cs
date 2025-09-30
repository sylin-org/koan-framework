using System.Collections.Concurrent;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Couchbase;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Koan.Core;
using Koan.Core.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Couchbase;

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
    private readonly HttpClient _httpClient;
    private ICluster? _cluster;
    private IBucket? _bucket;
    private string? _bucketName;
    private readonly ConcurrentDictionary<string, IScope> _scopes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ICouchbaseCollection> _collections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReadinessStateManager _stateManager = new();

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

    public Task<bool> IsReadyAsync(CancellationToken ct = default)
        => Task.FromResult(_stateManager.IsReady);

    public async Task WaitForReadinessAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
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
            await _stateManager.WaitAsync(effectiveTimeout, ct).ConfigureAwait(false);
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

    private async Task<bool> IsClusterInitializedAsync(string baseUrl, string username, string password, CancellationToken ct)
    {
        try
        {
            // Use a fresh HttpClient instance to avoid auth header contamination
            using var checkClient = new HttpClient();

            // Try with authentication - only properly initialized clusters accept specific credentials
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            checkClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            var response = await checkClient.GetAsync($"{baseUrl}/pools/default", ct).ConfigureAwait(false);

            // If 401 Unauthorized, cluster exists but our credentials are wrong (uninitialized or different creds)
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return false;
            }

            // If successful with our credentials, verify we can actually access admin functions
            if (response.IsSuccessStatusCode)
            {
                // Test if we can access the buckets endpoint - only works if properly initialized
                var bucketResponse = await checkClient.GetAsync($"{baseUrl}/pools/default/buckets", ct).ConfigureAwait(false);
                return bucketResponse.IsSuccessStatusCode;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task InitializeClusterAsync(string baseUrl, string username, string password, CancellationToken ct)
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
        var response = await initClient.PostAsync($"{baseUrl}/clusterInit", initData, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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

    private async Task<bool> WaitForCouchbaseAndCheckInitializationAsync(string baseUrl, string username, string password, string bucketName, CancellationToken ct)
    {
        const int maxRetries = 12; // Wait up to 60 seconds (5s * 12)
        const int delayMs = 5000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // Try to connect to Couchbase web console using clean client
                using var healthClient = new HttpClient();
                var response = await healthClient.GetAsync($"{baseUrl}/ui/index.html", ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("Couchbase web console is ready");

                    // Check if cluster is already initialized
                    if (await IsClusterInitializedAsync(baseUrl, username, password, ct).ConfigureAwait(false))
                    {
                        _logger?.LogDebug("Couchbase cluster is already initialized");
                        return true;
                    }

                    // Initialize cluster
                    await InitializeClusterAsync(baseUrl, username, password, ct).ConfigureAwait(false);

                    // Ensure bucket exists
                    await EnsureBucketExistsAsync(baseUrl, username, password, bucketName, ct).ConfigureAwait(false);

                    // Wait for N1QL service to become query-ready
                    await WaitForN1QLServiceReadinessAsync(baseUrl, username, password, ct).ConfigureAwait(false);

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Waiting for Couchbase to be ready (attempt {Attempt}/{MaxAttempts})", i + 1, maxRetries);
            }

            if (i < maxRetries - 1) // Don't wait on the last iteration
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        _logger?.LogWarning("Couchbase was not ready after {MaxRetries} attempts. Proceeding without initialization.", maxRetries);
        _stateManager.TransitionTo(AdapterReadinessState.Degraded);
        return false;
    }

    private async Task EnsureBucketExistsAsync(string baseUrl, string username, string password, string bucketName, CancellationToken ct)
    {
        try
        {
            // Check if bucket exists
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

            var response = await _httpClient.GetAsync($"{baseUrl}/pools/default/buckets/{bucketName}", ct).ConfigureAwait(false);

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

            var bucketResponse = await _httpClient.PostAsync($"{baseUrl}/pools/default/buckets", bucketData, ct).ConfigureAwait(false);

            if (!bucketResponse.IsSuccessStatusCode)
            {
                var error = await bucketResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
                await Task.Delay(2000, ct).ConfigureAwait(false);

                // Verify bucket is accessible
                for (int i = 0; i < 5; i++)
                {
                    var verifyResponse = await _httpClient.GetAsync($"{baseUrl}/pools/default/buckets/{bucketName}", ct).ConfigureAwait(false);
                    if (verifyResponse.IsSuccessStatusCode)
                    {
                        _logger?.LogDebug("Bucket {BucketName} verified accessible", bucketName);
                        break;
                    }
                    if (i < 4) await Task.Delay(1000, ct).ConfigureAwait(false);
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

    public async ValueTask<CouchbaseCollectionContext> GetCollectionContextAsync(string collectionName, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var cluster = await EnsureClusterAsync(options, ct).ConfigureAwait(false);
        var bucket = await EnsureBucketAsync(cluster, options, ct).ConfigureAwait(false);
        var scopeName = string.IsNullOrWhiteSpace(options.Scope) ? "_default" : options.Scope!;
        var scope = await GetScopeAsync(bucket, scopeName).ConfigureAwait(false);
        var finalCollection = string.IsNullOrWhiteSpace(collectionName)
            ? (!string.IsNullOrWhiteSpace(options.Collection) ? options.Collection! : "_default")
            : collectionName;
        var collection = await GetCollectionAsync(scope, scopeName, finalCollection).ConfigureAwait(false);
        if (_stateManager.State == AdapterReadinessState.Initializing)
        {
            _stateManager.TransitionTo(AdapterReadinessState.Ready);
        }
        return new CouchbaseCollectionContext(cluster, bucket, scope, collection, bucket.Name, scopeName, finalCollection);
    }

    private async ValueTask<ICluster> EnsureClusterAsync(CouchbaseOptions options, CancellationToken ct)
    {
        if (_cluster is not null)
        {
            return _cluster;
        }

        await _sync.WaitAsync(ct).ConfigureAwait(false);
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

                // Extract base URL for REST API calls
                var baseUrl = options.ConnectionString.Replace("couchbase://", "http://").TrimEnd('/');
                var schemeEnd = baseUrl.IndexOf("://") + 3;
                if (baseUrl.IndexOf(':', schemeEnd) == -1)
                {
                    baseUrl += ":8091";
                }

                // Wait for Couchbase to be ready and check if cluster needs initialization
                if (!await WaitForCouchbaseAndCheckInitializationAsync(baseUrl, username, password, options.Bucket, ct).ConfigureAwait(false))
                {
                    _logger?.LogDebug("Couchbase cluster initialization was not needed or failed gracefully");
                }

                var clusterOptions = new global::Couchbase.ClusterOptions
                {
                    UserName = username,
                    Password = password
                };

                _logger?.LogDebug("Connecting to Couchbase cluster at {ConnectionString}", Redaction.DeIdentify(options.ConnectionString));
                _cluster = await Cluster.ConnectAsync(options.ConnectionString, clusterOptions).ConfigureAwait(false);

                _logger?.LogDebug("Waiting for Couchbase cluster to finish bootstrapping");
                await _cluster.WaitUntilReadyAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                _logger?.LogDebug("Couchbase cluster bootstrap completed");
            }
        }
        finally
        {
            _sync.Release();
        }

        return _cluster!;
    }

    private async ValueTask<IBucket> EnsureBucketAsync(ICluster cluster, CouchbaseOptions options, CancellationToken ct)
    {
        if (_bucket is not null && string.Equals(_bucketName, options.Bucket, StringComparison.OrdinalIgnoreCase))
        {
            return _bucket;
        }

        await _sync.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_bucket is null || !string.Equals(_bucketName, options.Bucket, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Opening Couchbase bucket {Bucket}", options.Bucket);

                // Retry bucket access with exponential backoff for connection issues
                const int maxRetries = 5;
                const int baseDelayMs = 500;

                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        _bucket = await cluster.BucketAsync(options.Bucket).ConfigureAwait(false);

                        // Wait for bucket to be ready before proceeding
                        await _bucket.WaitUntilReadyAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

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
                        var delay = baseDelayMs * (int)Math.Pow(2, attempt);
                        _logger?.LogDebug(ex, "Bucket access attempt {Attempt}/{MaxRetries} failed, retrying in {Delay}ms", attempt + 1, maxRetries, delay);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
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

    private async ValueTask<IScope> GetScopeAsync(IBucket bucket, string scopeName)
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
            scope = await bucket.ScopeAsync(scopeName).ConfigureAwait(false);
        }

        _scopes[scopeName] = scope;
        return scope;
    }

    private async ValueTask<ICouchbaseCollection> GetCollectionAsync(IScope scope, string scopeName, string collectionName)
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
                collection = await scope.CollectionAsync(collectionName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Collection {CollectionName} not found, attempting to create it", collectionName);

                // Try to create the collection if it doesn't exist
                await EnsureCollectionExistsAsync(scope, scopeName, collectionName).ConfigureAwait(false);

                // Now try to get it again
                collection = await scope.CollectionAsync(collectionName).ConfigureAwait(false);
            }
        }

        _collections[key] = collection;
        return collection;
    }

    private async Task EnsureCollectionExistsAsync(IScope scope, string scopeName, string collectionName)
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
                    await collectionManager.CreateScopeAsync(scopeName).ConfigureAwait(false);
                }
                catch (global::Couchbase.CouchbaseException ex) when (IsAlreadyExists(ex))
                {
                    // Scope already exists, continue
                }
            }

            // Create the collection
            var settings = new CreateCollectionSettings();
            await collectionManager.CreateCollectionAsync(scopeName, collectionName, settings).ConfigureAwait(false);
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

    private async Task WaitForN1QLServiceReadinessAsync(string baseUrl, string username, string password, CancellationToken ct)
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

                var response = await queryClient.PostAsync($"{baseUrl}/query/service", queryData, ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        _logger?.LogWarning("N1QL service was not ready after {MaxRetries} attempts. Service may still be initializing.", maxRetries);
        // Don't fail - transition to degraded state and let the application handle N1QL unavailability
        _stateManager.TransitionTo(AdapterReadinessState.Degraded);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _stateManager.TransitionTo(AdapterReadinessState.Initializing);

        try
        {
            var options = _options.CurrentValue;
            var collectionName = options.Collection ?? string.Empty;
            await GetCollectionContextAsync(collectionName, ct).ConfigureAwait(false);

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
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _cluster?.Dispose();
        }

        _httpClient?.Dispose();
        _sync?.Dispose();
    }
}
