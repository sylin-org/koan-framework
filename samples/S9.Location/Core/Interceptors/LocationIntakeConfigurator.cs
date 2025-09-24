using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Flow.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S9.Location.Core.Diagnostics;
using S9.Location.Core.Models;
using S9.Location.Core.Options;
using S9.Location.Core.Services;

namespace S9.Location.Core.Interceptors;

public sealed class LocationIntakeConfigurator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocationIntakeConfigurator> _logger;
    private bool _configured;

    public LocationIntakeConfigurator(IServiceProvider serviceProvider, ILogger<LocationIntakeConfigurator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_configured)
        {
            return Task.CompletedTask;
        }

        FlowInterceptors.For<RawLocation>().BeforeIntake(async entity =>
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var normalization = scope.ServiceProvider.GetRequiredService<INormalizationService>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<LocationOptions>>().Value;
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<LocationIntakeConfigurator>>();

            if (string.IsNullOrWhiteSpace(entity.Address))
            {
                logger.LogWarning("Dropping location {SourceSystem}/{SourceId} because the address is empty", entity.SourceSystem, entity.SourceId);
                return FlowIntakeActions.Drop(entity, LocationFlowConstants.ParkedInvalidPayload);
            }

            var normalizationResult = normalization.Normalize(entity.SourceSystem, entity.Address);
            entity.Metadata ??= new Dictionary<string, object?>();
            entity.NormalizedAddress = normalizationResult.Normalized;
            entity.AddressHash = normalizationResult.Hash;

            if (options.Cache.Enabled && !string.IsNullOrWhiteSpace(normalizationResult.Hash))
            {
                var cached = await ResolutionCache.Get(normalizationResult.Hash);
                if (cached is not null)
                {
                    entity.CanonicalLocationId = cached.CanonicalLocationId;
                    entity.Metadata["canonical_id"] = cached.CanonicalLocationId;
                    logger.LogDebug("Cache hit for {HashPrefix}", normalizationResult.Hash[..Math.Min(normalizationResult.Hash.Length, 8)]);
                    return FlowIntakeActions.Continue(entity);
                }
            }

            logger.LogDebug("Parking location {SourceSystem}/{SourceId} for harmonization", entity.SourceSystem, entity.SourceId);
            return FlowIntakeActions.Park(entity, LocationFlowConstants.ParkedWaitingForResolution);
        });

        _configured = true;
        _logger.LogInformation("Registered Flow intake interceptor for RawLocation");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
