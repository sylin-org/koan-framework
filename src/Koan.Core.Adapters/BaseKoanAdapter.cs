using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Orchestration.Models;

namespace Koan.Core.Adapters;

/// <summary>
/// Base implementation for Koan adapters providing common functionality
/// and DX-focused patterns for adapter development.
/// </summary>
public abstract class BaseKoanAdapter : IKoanAdapter
{
    protected ILogger Logger { get; }
    protected IConfiguration Configuration { get; }

    protected BaseKoanAdapter(ILogger logger, IConfiguration configuration)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    // IKoanAdapter implementation
    public abstract ServiceType ServiceType { get; }
    public abstract string AdapterId { get; }
    public abstract string DisplayName { get; }
    public abstract AdapterCapabilities Capabilities { get; }

    // For compatibility with existing interfaces that expect Name
    public virtual string Name => DisplayName;

    // IHealthContributor implementation
    public virtual bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var metadata = await CheckAdapterHealthAsync(ct);
            var state = metadata?.ContainsKey("status") == true && metadata["status"]?.ToString() == "unhealthy"
                ? HealthState.Unhealthy
                : HealthState.Healthy;

            return new HealthReport(AdapterId, state, null, null, metadata);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{AdapterId}] Health check failed", AdapterId);
            return new HealthReport(AdapterId, HealthState.Unhealthy, ex.Message, null, null);
        }
    }

    async Task<IReadOnlyDictionary<string, object?>?> IKoanAdapter.GetBootstrapMetadataAsync(CancellationToken cancellationToken)
    {
        return await GetAdapterBootstrapMetadataAsync(cancellationToken);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("[{AdapterId}] Initializing adapter", AdapterId);
        await InitializeAdapterAsync(cancellationToken);
        Logger.LogInformation("[{AdapterId}] Adapter initialized successfully", AdapterId);
    }

    public bool SupportsCapability<T>(T capability) where T : Enum
    {
        return capability switch
        {
            HealthCapabilities health => Capabilities.SupportsHealth(health),
            ConfigurationCapabilities config => Capabilities.SupportsConfiguration(config),
            SecurityCapabilities security => Capabilities.SupportsSecurity(security),
            MessagingCapabilities messaging => Capabilities.SupportsMessaging(messaging),
            OrchestrationCapabilities orchestration => Capabilities.SupportsOrchestration(orchestration),
            ExtendedQueryCapabilities data => Capabilities.SupportsData(data),
            _ => false
        };
    }

    // Template methods for derived classes
    protected abstract Task InitializeAdapterAsync(CancellationToken cancellationToken = default);
    protected abstract Task<IReadOnlyDictionary<string, object?>?> CheckAdapterHealthAsync(CancellationToken cancellationToken = default);
    protected abstract Task<IReadOnlyDictionary<string, object?>?> GetAdapterBootstrapMetadataAsync(CancellationToken cancellationToken = default);

    // Configuration helpers
    protected TOptions GetOptions<TOptions>() where TOptions : class, new()
    {
        var sectionName = $"Koan:Services:{AdapterId}";
        var section = Configuration.GetSection(sectionName);

        var options = section.Get<TOptions>();
        if (options == null)
        {
            // Try legacy patterns for backward compatibility
            var legacySections = new[]
            {
                $"Koan:AI:{AdapterId}",
                $"Koan:Data:{AdapterId}",
                $"Koan:Cache:{AdapterId}",
                AdapterId
            };

            foreach (var legacySection in legacySections)
            {
                section = Configuration.GetSection(legacySection);
                options = section.Get<TOptions>();
                if (options != null) break;
            }
        }

        return options ?? new TOptions();
    }

    protected string? GetConnectionString(string? name = null)
    {
        var connectionName = name ?? AdapterId;

        // Try standard connection strings first
        var connectionString = Configuration.GetConnectionString(connectionName);
        if (!string.IsNullOrEmpty(connectionString))
            return connectionString;

        // Try service-specific configuration
        var serviceSection = $"Koan:Services:{AdapterId}:ConnectionString";
        connectionString = Configuration[serviceSection];
        if (!string.IsNullOrEmpty(connectionString))
            return connectionString;

        // Try legacy patterns
        var legacyPatterns = new[]
        {
            $"Koan:AI:{AdapterId}:BaseUrl",
            $"Koan:Data:{AdapterId}:ConnectionString",
            $"Koan:Cache:{AdapterId}:ConnectionString",
            $"{AdapterId}:ConnectionString"
        };

        foreach (var pattern in legacyPatterns)
        {
            connectionString = Configuration[pattern];
            if (!string.IsNullOrEmpty(connectionString))
                return connectionString;
        }

        return null;
    }

    protected bool IsEnabled()
    {
        var enabledSection = $"Koan:Services:{AdapterId}:Enabled";
        if (Configuration[enabledSection] != null)
        {
            return Configuration.GetValue<bool>(enabledSection, true);
        }

        // Check legacy patterns
        var legacyPatterns = new[]
        {
            $"Koan:AI:{AdapterId}:Enabled",
            $"Koan:Data:{AdapterId}:Enabled",
            $"Koan:Cache:{AdapterId}:Enabled",
            $"{AdapterId}:Enabled"
        };

        foreach (var pattern in legacyPatterns)
        {
            if (Configuration[pattern] != null)
            {
                return Configuration.GetValue<bool>(pattern, true);
            }
        }

        return true; // Default to enabled
    }
}