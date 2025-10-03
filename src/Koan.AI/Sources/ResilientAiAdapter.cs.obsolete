using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Sources;
using Koan.AI.Sources.Policies;

namespace Koan.AI.Sources;

/// <summary>
/// Resilient adapter wrapper that implements group-based failover with circuit breakers.
/// Tries sources according to group policy, tracks health, and automatically fails over to healthy sources.
/// </summary>
public sealed class ResilientAiAdapter : IAiAdapter
{
    private readonly string _groupName;
    private readonly IAiSourceRegistry _sourceRegistry;
    private readonly ISourceHealthRegistry _healthRegistry;
    private readonly IGroupPolicy _policy;
    private readonly ILogger<ResilientAiAdapter>? _logger;

    public ResilientAiAdapter(
        string groupName,
        IAiSourceRegistry sourceRegistry,
        ISourceHealthRegistry healthRegistry,
        IGroupPolicy policy,
        ILogger<ResilientAiAdapter>? logger = null)
    {
        _groupName = groupName;
        _sourceRegistry = sourceRegistry;
        _healthRegistry = healthRegistry;
        _policy = policy;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => $"resilient-group:{_groupName}";

    /// <inheritdoc />
    public string Name => $"Resilient Group: {_groupName}";

    /// <inheritdoc />
    public string Type => "resilient-group";

    /// <inheritdoc />
    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var sources = GetGroupSources();
        if (sources.Count == 0)
        {
            throw new InvalidOperationException($"No sources available in group '{_groupName}'");
        }

        Exception? lastException = null;
        var attemptedSources = new List<string>();

        // Try sources according to policy (respecting circuit breaker state)
        foreach (var source in sources)
        {
            if (!_healthRegistry.IsAvailable(source.Name))
            {
                _logger?.LogDebug(
                    "Skipping unavailable source '{SourceName}' (circuit {State})",
                    source.Name,
                    _healthRegistry.GetHealth(source.Name).State);
                continue;
            }

            var adapter = _policy.SelectAdapter(new[] { source }, _healthRegistry);
            if (adapter == null)
            {
                continue;
            }

            attemptedSources.Add(source.Name);

            try
            {
                _logger?.LogDebug(
                    "Attempting chat request via source '{SourceName}' (adapter '{AdapterId}')",
                    source.Name,
                    adapter.Id);

                var response = await adapter.ChatAsync(request, ct).ConfigureAwait(false);

                _healthRegistry.RecordSuccess(source.Name);

                if (attemptedSources.Count > 1)
                {
                    _logger?.LogInformation(
                        "Chat request succeeded via fallback source '{SourceName}' after {AttemptCount} attempts",
                        source.Name,
                        attemptedSources.Count);
                }

                return response;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _healthRegistry.RecordFailure(source.Name);

                _logger?.LogWarning(
                    ex,
                    "Chat request failed via source '{SourceName}': {ErrorMessage}",
                    source.Name,
                    ex.Message);

                // Continue to next source
            }
        }

        // All sources failed
        var message = attemptedSources.Count > 0
            ? $"All {attemptedSources.Count} sources in group '{_groupName}' failed. Attempted: {string.Join(", ", attemptedSources)}"
            : $"No available sources in group '{_groupName}'";

        throw new AggregateException(message, lastException ?? new InvalidOperationException("No sources available"));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AiChatChunk> StreamAsync(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sources = GetGroupSources();
        if (sources.Count == 0)
        {
            throw new InvalidOperationException($"No sources available in group '{_groupName}'");
        }

        Exception? lastException = null;
        var attemptedSources = new List<string>();
        List<AiChatChunk>? successfulChunks = null;

        // Try sources according to policy (respecting circuit breaker state)
        foreach (var source in sources)
        {
            if (!_healthRegistry.IsAvailable(source.Name))
            {
                continue;
            }

            var adapter = _policy.SelectAdapter(new[] { source }, _healthRegistry);
            if (adapter == null)
            {
                continue;
            }

            attemptedSources.Add(source.Name);

            IAsyncEnumerator<AiChatChunk>? enumerator = null;
            var chunks = new List<AiChatChunk>();

            try
            {
                _logger?.LogDebug(
                    "Attempting stream request via source '{SourceName}' (adapter '{AdapterId}')",
                    source.Name,
                    adapter.Id);

                enumerator = adapter.StreamAsync(request, ct).GetAsyncEnumerator(ct);

                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    chunks.Add(enumerator.Current);
                }

                _healthRegistry.RecordSuccess(source.Name);

                if (attemptedSources.Count > 1)
                {
                    _logger?.LogInformation(
                        "Stream request succeeded via fallback source '{SourceName}' after {AttemptCount} attempts",
                        source.Name,
                        attemptedSources.Count);
                }

                // Success - save chunks to yield outside try/catch
                successfulChunks = chunks;
                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _healthRegistry.RecordFailure(source.Name);

                _logger?.LogWarning(
                    ex,
                    "Stream request failed via source '{SourceName}': {ErrorMessage}",
                    source.Name,
                    ex.Message);

                // Continue to next source
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        // Yield results outside try/catch (C# limitation: can't yield in try/catch)
        if (successfulChunks != null)
        {
            foreach (var chunk in successfulChunks)
            {
                yield return chunk;
            }
            yield break;
        }

        // All sources failed
        var message = attemptedSources.Count > 0
            ? $"All {attemptedSources.Count} sources in group '{_groupName}' failed. Attempted: {string.Join(", ", attemptedSources)}"
            : $"No available sources in group '{_groupName}'";

        throw new AggregateException(message, lastException ?? new InvalidOperationException("No sources available"));
    }

    /// <inheritdoc />
    public async Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var sources = GetGroupSources();
        if (sources.Count == 0)
        {
            throw new InvalidOperationException($"No sources available in group '{_groupName}'");
        }

        Exception? lastException = null;
        var attemptedSources = new List<string>();

        // Try sources according to policy (respecting circuit breaker state)
        foreach (var source in sources)
        {
            if (!_healthRegistry.IsAvailable(source.Name))
            {
                continue;
            }

            var adapter = _policy.SelectAdapter(new[] { source }, _healthRegistry);
            if (adapter == null)
            {
                continue;
            }

            attemptedSources.Add(source.Name);

            try
            {
                _logger?.LogDebug(
                    "Attempting embed request via source '{SourceName}' (adapter '{AdapterId}')",
                    source.Name,
                    adapter.Id);

                var response = await adapter.EmbedAsync(request, ct).ConfigureAwait(false);

                _healthRegistry.RecordSuccess(source.Name);

                if (attemptedSources.Count > 1)
                {
                    _logger?.LogInformation(
                        "Embed request succeeded via fallback source '{SourceName}' after {AttemptCount} attempts",
                        source.Name,
                        attemptedSources.Count);
                }

                return response;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _healthRegistry.RecordFailure(source.Name);

                _logger?.LogWarning(
                    ex,
                    "Embed request failed via source '{SourceName}': {ErrorMessage}",
                    source.Name,
                    ex.Message);

                // Continue to next source
            }
        }

        // All sources failed
        var message = attemptedSources.Count > 0
            ? $"All {attemptedSources.Count} sources in group '{_groupName}' failed. Attempted: {string.Join(", ", attemptedSources)}"
            : $"No available sources in group '{_groupName}'";

        throw new AggregateException(message, lastException ?? new InvalidOperationException("No sources available"));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
    {
        // For group adapters, aggregate models from all sources
        var sources = GetGroupSources();
        var allModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (!_healthRegistry.IsAvailable(source.Name))
            {
                continue;
            }

            var adapter = _policy.SelectAdapter(new[] { source }, _healthRegistry);
            if (adapter == null)
            {
                continue;
            }

            try
            {
                var models = await adapter.ListModelsAsync(ct).ConfigureAwait(false);
                foreach (var model in models)
                {
                    allModels.Add(model.Name);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(
                    ex,
                    "Failed to list models from source '{SourceName}': {ErrorMessage}",
                    source.Name,
                    ex.Message);
                // Continue to next source
            }
        }

        return allModels
            .Select(name => new AiModelDescriptor { Name = name })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<AiCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        // For group adapters, aggregate capabilities from all sources
        var sources = GetGroupSources();
        var supportsChat = false;
        var supportsStreaming = false;
        var supportsEmbeddings = false;

        foreach (var source in sources)
        {
            if (!_healthRegistry.IsAvailable(source.Name))
            {
                continue;
            }

            var adapter = _policy.SelectAdapter(new[] { source }, _healthRegistry);
            if (adapter == null)
            {
                continue;
            }

            try
            {
                var sourceCaps = await adapter.GetCapabilitiesAsync(ct).ConfigureAwait(false);

                // Aggregate capabilities (if any source supports it, group supports it)
                supportsChat |= sourceCaps.SupportsChat;
                supportsStreaming |= sourceCaps.SupportsStreaming;
                supportsEmbeddings |= sourceCaps.SupportsEmbeddings;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(
                    ex,
                    "Failed to get capabilities from source '{SourceName}': {ErrorMessage}",
                    source.Name,
                    ex.Message);
                // Continue to next source
            }
        }

        return new AiCapabilities
        {
            AdapterId = Id,
            AdapterType = Type,
            SupportsChat = supportsChat,
            SupportsStreaming = supportsStreaming,
            SupportsEmbeddings = supportsEmbeddings
        };
    }

    /// <inheritdoc />
    public bool CanServe(AiChatRequest request)
    {
        // Resilient adapter can serve if any source in group can serve
        var sources = GetGroupSources();
        return sources.Count > 0;
    }

    private IReadOnlyList<AiSourceDefinition> GetGroupSources()
    {
        return _sourceRegistry.GetSourcesInGroup(_groupName).ToList();
    }
}
