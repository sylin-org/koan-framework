using System.Security.Cryptography;
using System.Text;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Koan.Context.Services;

/// <summary>
/// Embedding service with caching and retry support
/// </summary>
/// <remarks>
/// Wraps IAi.EmbedAsync with SHA256-based caching to avoid redundant API calls.
/// Cache keys are based on hash(text + modelId) for deterministic lookups.
/// QA Issue #21 FIXED: Added Polly retry for transient failures.
/// QA Issue #31 FIXED: Exponential backoff with jitter.
/// </remarks>
public class Embedding
{
    private readonly IAi _ai;
    private readonly IMemoryCache _cache;
    private readonly ILogger<Embedding> _logger;
    private readonly string? _defaultModel;
    private readonly AsyncRetryPolicy _retryPolicy;

    public Embedding(
        IAi ai,
        IMemoryCache cache,
        ILogger<Embedding> logger,
        string? defaultModel = null)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultModel = defaultModel;

        // QA Issue #21 FIX: Retry policy for transient failures
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) +
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100)), // Jitter
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Embedding API call failed (attempt {RetryCount}/3). Retrying after {Delay}ms",
                        retryCount,
                        timeSpan.TotalMilliseconds);
                });
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // QA Issue #34 DECISION: Skip empty text instead of throwing
            _logger.LogDebug("Empty text provided to EmbedAsync, returning empty embedding");
            return Array.Empty<float>();
        }

        var cacheKey = ComputeCacheKey(text, _defaultModel);

        // Check cache first
        if (_cache.TryGetValue<float[]>(cacheKey, out var cachedEmbedding))
        {
            _logger.LogDebug("Embedding cache hit for text length {TextLength}", text.Length);
            return cachedEmbedding!;
        }

        // Generate embedding with retry
        try
        {
            var embedding = await _retryPolicy.ExecuteAsync(async () =>
            {
                var request = new AiEmbeddingsRequest
                {
                    Model = _defaultModel
                };
                request.Input.Add(text);

                var response = await _ai.EmbedAsync(request, cancellationToken);

                if (response.Vectors.Count == 0)
                {
                    throw new InvalidOperationException("Embedding provider returned no vectors");
                }

                return response.Vectors[0];
            });

            // Cache the result (expire after 24 hours)
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
                Size = embedding.Length * sizeof(float) // Approximate memory size
            };

            _cache.Set(cacheKey, embedding, cacheOptions);

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to generate embedding after 3 retries for text (length: {TextLength})",
                text.Length);
            throw;
        }
    }

    public async Task<Dictionary<string, float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var result = new Dictionary<string, float[]>();

        if (textList.Count == 0)
        {
            return result;
        }

        // Separate cached and uncached texts
        var uncachedTexts = new List<string>();
        var uncachedKeys = new List<string>();
        var uncachedIndices = new Dictionary<string, int>(); // QA Issue #22 FIX: Track original order

        for (int i = 0; i < textList.Count; i++)
        {
            var text = textList[i];

            if (string.IsNullOrWhiteSpace(text))
            {
                // Skip empty texts
                continue;
            }

            var cacheKey = ComputeCacheKey(text, _defaultModel);

            if (_cache.TryGetValue<float[]>(cacheKey, out var cachedEmbedding))
            {
                result[text] = cachedEmbedding!;
            }
            else
            {
                uncachedTexts.Add(text);
                uncachedKeys.Add(cacheKey);
                uncachedIndices[text] = i; // Store original index
            }
        }

        _logger.LogInformation(
            "Embedding batch: {Total} texts, {Cached} from cache, {Uncached} to generate",
            textList.Count,
            result.Count,
            uncachedTexts.Count);

        // Generate embeddings for uncached texts with retry
        if (uncachedTexts.Count > 0)
        {
            try
            {
                var embeddings = await _retryPolicy.ExecuteAsync(async () =>
                {
                    var request = new AiEmbeddingsRequest
                    {
                        Model = _defaultModel
                    };
                    request.Input.AddRange(uncachedTexts);

                    var response = await _ai.EmbedAsync(request, cancellationToken);

                    if (response.Vectors.Count != uncachedTexts.Count)
                    {
                        throw new InvalidOperationException(
                            $"Embedding provider returned {response.Vectors.Count} vectors but expected {uncachedTexts.Count}");
                    }

                    return response.Vectors;
                });

                // Cache and add to results (preserving order via indices)
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                };

                for (int i = 0; i < uncachedTexts.Count; i++)
                {
                    var text = uncachedTexts[i];
                    var embedding = embeddings[i];
                    var cacheKey = uncachedKeys[i];

                    _cache.Set(cacheKey, embedding, cacheOptions);
                    result[text] = embedding;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to generate batch embeddings after 3 retries ({Count} texts)",
                    uncachedTexts.Count);
                throw;
            }
        }

        return result;
    }

    private static string ComputeCacheKey(string text, string? model)
    {
        var input = $"{text}|{model ?? "default"}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"embedding:{Convert.ToHexString(hashBytes)}";
    }
}
