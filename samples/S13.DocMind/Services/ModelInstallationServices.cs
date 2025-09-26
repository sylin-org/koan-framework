using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace S13.DocMind.Services;

public interface IModelInstallationQueue
{
    Task<ModelInstallationStatus> EnqueueAsync(string provider, string modelName, CancellationToken cancellationToken);
    IAsyncEnumerable<ModelInstallationStatus> DequeueAsync(CancellationToken cancellationToken);
    IReadOnlyCollection<ModelInstallationStatus> GetStatuses();
    ModelInstallationStatus? GetStatus(Guid installationId);
}

public class InMemoryModelInstallationQueue : IModelInstallationQueue
{
    private readonly Channel<ModelInstallationStatus> _channel;
    private readonly ConcurrentDictionary<Guid, ModelInstallationStatus> _statuses = new();

    public InMemoryModelInstallationQueue()
    {
        _channel = Channel.CreateUnbounded<ModelInstallationStatus>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public async Task<ModelInstallationStatus> EnqueueAsync(string provider, string modelName, CancellationToken cancellationToken)
    {
        var status = new ModelInstallationStatus
        {
            InstallationId = Guid.NewGuid(),
            Provider = provider,
            ModelName = modelName,
            State = ModelInstallationState.Queued,
            Progress = 0,
            CurrentStep = "Queued",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        _statuses[status.InstallationId] = status;
        await _channel.Writer.WriteAsync(status, cancellationToken);
        return status;
    }

    public async IAsyncEnumerable<ModelInstallationStatus> DequeueAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var status))
            {
                yield return status;
            }
        }
    }

    public IReadOnlyCollection<ModelInstallationStatus> GetStatuses()
        => _statuses.Values
            .OrderByDescending(status => status.EnqueuedAt)
            .ToList();

    public ModelInstallationStatus? GetStatus(Guid installationId)
        => _statuses.TryGetValue(installationId, out var status) ? status : null;
}

public class ModelInstallationBackgroundService : BackgroundService
{
    private static readonly string[] InstallationSteps =
    {
        "Validating model", "Downloading model", "Optimizing weights", "Registering provider", "Finalizing"
    };

    private readonly IModelInstallationQueue _queue;
    private readonly IModelCatalogService _catalogService;
    private readonly ILogger<ModelInstallationBackgroundService> _logger;

    public ModelInstallationBackgroundService(
        IModelInstallationQueue queue,
        IModelCatalogService catalogService,
        ILogger<ModelInstallationBackgroundService> logger)
    {
        _queue = queue;
        _catalogService = catalogService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var status in _queue.DequeueAsync(stoppingToken))
        {
            try
            {
                status.State = ModelInstallationState.Installing;
                status.StartedAt = DateTimeOffset.UtcNow;

                var stepProgress = 100 / InstallationSteps.Length;
                for (var index = 0; index < InstallationSteps.Length; index++)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    status.CurrentStep = InstallationSteps[index];
                    status.Progress = Math.Min(95, (index + 1) * stepProgress);
                    status.LastUpdated = DateTimeOffset.UtcNow;
                    await Task.Delay(TimeSpan.FromMilliseconds(350), stoppingToken);
                }

                status.Progress = 100;
                status.State = ModelInstallationState.Completed;
                status.CompletedAt = DateTimeOffset.UtcNow;
                status.LastUpdated = status.CompletedAt;
                status.CurrentStep = "Completed";

                await _catalogService.MarkInstalledAsync(status.Provider, status.ModelName, stoppingToken);
                _logger.LogInformation("Model {ModelName} installed for provider {Provider}", status.ModelName, status.Provider);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                status.State = ModelInstallationState.Failed;
                status.LastUpdated = DateTimeOffset.UtcNow;
                status.ErrorMessage = ex.Message;
                status.CurrentStep = "Failed";
                _logger.LogError(ex, "Failed to install model {ModelName}", status.ModelName);
            }
        }
    }
}

public interface IModelCatalogService
{
    Task<IReadOnlyCollection<ModelDescriptor>> GetAvailableModelsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ModelDescriptor>> GetInstalledModelsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ModelDescriptor>> SearchModelsAsync(string? query, string? provider, CancellationToken cancellationToken);
    Task<ModelConfigurationSnapshot> GetConfigurationAsync(CancellationToken cancellationToken);
    Task SetDefaultModelAsync(string kind, string modelName, string provider, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ModelProviderStatus>> GetProvidersAsync(CancellationToken cancellationToken);
    Task<ModelHealthReport> GetHealthAsync(CancellationToken cancellationToken);
    Task<ModelUsageStatistics> GetUsageAsync(CancellationToken cancellationToken);
    Task MarkInstalledAsync(string provider, string modelName, CancellationToken cancellationToken);
}

public class InMemoryModelCatalogService : IModelCatalogService
{
    private readonly List<ModelDescriptor> _availableModels = new()
    {
        new("gpt-4.1-mini", "openai", "OpenAI GPT-4.1 Mini", new[] { "text" }, "7B", false),
        new("gpt-4o", "openai", "GPT-4 Omni", new[] { "text", "vision" }, "15B", true),
        new("claude-3-5-sonnet", "anthropic", "Claude 3.5 Sonnet", new[] { "text", "analysis" }, "12B", false),
        new("llama3.1:8b", "ollama", "Llama 3.1 8B", new[] { "text" }, "8B", false),
        new("llava:13b", "ollama", "LLaVA 13B Vision", new[] { "vision", "text" }, "13B", true)
    };

    private readonly ConcurrentDictionary<string, ModelDescriptor> _installedModels = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = "gpt-4.1-mini|openai",
        ["vision"] = "gpt-4o|openai"
    };

    public Task<IReadOnlyCollection<ModelDescriptor>> GetAvailableModelsAsync(CancellationToken cancellationToken)
    {
        var installedKeys = new HashSet<string>(_installedModels.Keys, StringComparer.OrdinalIgnoreCase);
        var models = _availableModels
            .Select(model =>
            {
                var key = $"{model.Provider}:{model.Name}";
                return installedKeys.Contains(key)
                    ? model with { IsInstalled = true }
                    : model with { IsInstalled = false };
            })
            .ToList();

        return Task.FromResult<IReadOnlyCollection<ModelDescriptor>>(models);
    }

    public Task<IReadOnlyCollection<ModelDescriptor>> GetInstalledModelsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<ModelDescriptor>>(_installedModels.Values.ToList());

    public Task<IReadOnlyCollection<ModelDescriptor>> SearchModelsAsync(string? query, string? provider, CancellationToken cancellationToken)
    {
        var installedKeys = new HashSet<string>(_installedModels.Keys, StringComparer.OrdinalIgnoreCase);
        var results = _availableModels.Where(model =>
            (string.IsNullOrEmpty(query) || model.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || model.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(provider) || string.Equals(model.Provider, provider, StringComparison.OrdinalIgnoreCase)))
            .Select(model =>
            {
                var key = $"{model.Provider}:{model.Name}";
                return installedKeys.Contains(key)
                    ? model with { IsInstalled = true }
                    : model with { IsInstalled = false };
            })
            .ToList();

        return Task.FromResult<IReadOnlyCollection<ModelDescriptor>>(results);
    }

    public Task<ModelConfigurationSnapshot> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        var defaults = _defaults.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var components = pair.Value.Split('|');
                return new ModelSelection
                {
                    ModelName = components[0],
                    Provider = components.Length > 1 ? components[1] : string.Empty
                };
            },
            StringComparer.OrdinalIgnoreCase);

        var snapshot = new ModelConfigurationSnapshot
        {
            AvailableProviders = new[] { "openai", "ollama", "anthropic" },
            Defaults = defaults
        };

        return Task.FromResult(snapshot);
    }

    public Task SetDefaultModelAsync(string kind, string modelName, string provider, CancellationToken cancellationToken)
    {
        _defaults[kind] = $"{modelName}|{provider}";
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ModelProviderStatus>> GetProvidersAsync(CancellationToken cancellationToken)
    {
        var providers = new List<ModelProviderStatus>
        {
            new("openai", true, "Managed OpenAI provider"),
            new("ollama", true, "Local Ollama runtime"),
            new("anthropic", false, "Anthropic provider disabled"),
        };

        return Task.FromResult<IReadOnlyCollection<ModelProviderStatus>>(providers);
    }

    public Task<ModelHealthReport> GetHealthAsync(CancellationToken cancellationToken)
    {
        var report = new ModelHealthReport
        {
            HealthyProviders = new[] { "openai", "ollama" },
            DegradedProviders = Array.Empty<string>(),
            FailedProviders = new[] { "anthropic" }
        };

        return Task.FromResult(report);
    }

    public Task<ModelUsageStatistics> GetUsageAsync(CancellationToken cancellationToken)
    {
        var usage = new ModelUsageStatistics
        {
            TotalAnalyses = 128,
            TotalTokens = 523_000,
            ProviderUsage = new Dictionary<string, int>
            {
                ["openai"] = 84,
                ["ollama"] = 32,
                ["anthropic"] = 12
            }
        };

        return Task.FromResult(usage);
    }

    public Task MarkInstalledAsync(string provider, string modelName, CancellationToken cancellationToken)
    {
        var descriptor = _availableModels.FirstOrDefault(model =>
            string.Equals(model.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(model.Name, modelName, StringComparison.OrdinalIgnoreCase));

        if (descriptor is not null)
        {
            _installedModels[$"{provider}:{modelName}"] = descriptor with { IsInstalled = true };
        }

        return Task.CompletedTask;
    }
}

public record ModelDescriptor(string Name, string Provider, string DisplayName, IReadOnlyCollection<string> Capabilities, string? Size, bool IsVisionCapable)
{
    public bool IsInstalled { get; init; }
}

public class ModelInstallationStatus
{
    public Guid InstallationId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public ModelInstallationState State { get; set; }
    public int Progress { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? LastUpdated { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum ModelInstallationState
{
    Queued,
    Installing,
    Completed,
    Failed
}

public class ModelConfigurationSnapshot
{
    public IReadOnlyCollection<string> AvailableProviders { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, ModelSelection> Defaults { get; set; } = new Dictionary<string, ModelSelection>();
}

public class ModelSelection
{
    public string ModelName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
}

public record ModelProviderStatus(string Provider, bool Enabled, string Description);

public class ModelHealthReport
{
    public IReadOnlyCollection<string> HealthyProviders { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<string> DegradedProviders { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<string> FailedProviders { get; set; } = Array.Empty<string>();
}

public class ModelUsageStatistics
{
    public int TotalAnalyses { get; set; }
    public int TotalTokens { get; set; }
    public IReadOnlyDictionary<string, int> ProviderUsage { get; set; } = new Dictionary<string, int>();
}
