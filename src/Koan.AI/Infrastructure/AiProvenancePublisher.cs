using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Pillars;
using Koan.Core.Provenance;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Infrastructure;

/// <summary>
/// Emits live adapter and source health snapshots into provenance once the application starts.
/// </summary>
internal sealed class AiProvenancePublisher : BackgroundService
{
    private readonly IAiAdapterRegistry _adapters;
    private readonly IAiSourceRegistry _sources;
    private readonly ILogger<AiProvenancePublisher> _logger;

    public AiProvenancePublisher(
        IAiAdapterRegistry adapters,
        IAiSourceRegistry sources,
        ILogger<AiProvenancePublisher> logger)
    {
        _adapters = adapters;
        _sources = sources;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Allow the hosting pipeline to finish building adapters before capturing state.
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await PublishSnapshotAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when host is shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish AI provenance snapshot");
        }
    }

    private async Task PublishSnapshotAsync(CancellationToken ct)
    {
        var registry = ProvenanceRegistry.Instance;
        var module = registry.GetOrCreateModule(AiPillarManifest.PillarCode, "Koan.AI");

        await PublishAdapterRosterAsync(module, ct).ConfigureAwait(false);
        PublishSourceHealth(module);
    }

    private Task PublishAdapterRosterAsync(ProvenanceModuleWriter module, CancellationToken ct)
    {
        var summaries = new List<string>();

        foreach (var adapter in _adapters.All)
        {
            var summary = new StringBuilder();
            summary.Append(adapter.Id);
            summary.Append(" (type=").Append(adapter.Type).Append(')');

            // Structural capability detection via ISP interfaces
            summary.Append(" chat=").Append(adapter is Contracts.Adapters.IChatAdapter ? "yes" : "no");
            summary.Append(" embed=").Append(adapter is Contracts.Adapters.IEmbedAdapter ? "yes" : "no");
            summary.Append(" ocr=").Append(adapter is Contracts.Adapters.IOcrAdapter ? "yes" : "no");

            if (adapter.ModelManager is not null)
            {
                summary.Append(" provision=yes");
            }

            summaries.Add(summary.ToString());
        }

        module.SetSetting(KoanAiProvenanceItems.AdapterRoster.Key, setting => setting
            .Label(KoanAiProvenanceItems.AdapterRoster.Label)
            .Description(KoanAiProvenanceItems.AdapterRoster.Description)
            .Value(summaries.Count > 0 ? string.Join("; ", summaries) : "(none)")
            .Source(ProvenanceSettingSource.Custom)
            .Consumers(KoanAiProvenanceItems.AdapterRoster.DefaultConsumers?.ToArray() ?? Array.Empty<string>())
            .State(ProvenanceSettingState.Configured));

        return Task.CompletedTask;
    }

    private void PublishSourceHealth(ProvenanceModuleWriter module)
    {
        var sources = _sources.GetAllSources();
        if (sources.Count == 0)
        {
            module.SetSetting(KoanAiProvenanceItems.SourceMemberStatus.Key, setting => setting
                .Label(KoanAiProvenanceItems.SourceMemberStatus.Label)
                .Description(KoanAiProvenanceItems.SourceMemberStatus.Description)
                .Value("(no sources registered)")
                .Source(ProvenanceSettingSource.Custom)
                .Consumers(KoanAiProvenanceItems.SourceMemberStatus.DefaultConsumers?.ToArray() ?? Array.Empty<string>())
                .State(ProvenanceSettingState.Default));
            return;
        }

        var details = new List<string>(sources.Count);
        foreach (var source in sources.OrderByDescending(s => s.Priority))
        {
            var memberSummaries = source.Members.Select(member =>
            {
                var health = member.HealthState.ToString();
                return $"{member.Name} [{health}]";
            });

            var healthState = source.GetHealthState();
            details.Add($"{source.Name} (priority={source.Priority}, policy={source.Policy}, health={healthState}): {string.Join(", ", memberSummaries)}");
        }

        module.SetSetting(KoanAiProvenanceItems.SourceMemberStatus.Key, setting => setting
            .Label(KoanAiProvenanceItems.SourceMemberStatus.Label)
            .Description(KoanAiProvenanceItems.SourceMemberStatus.Description)
            .Value(string.Join("; ", details))
            .Source(ProvenanceSettingSource.Custom)
            .Consumers(KoanAiProvenanceItems.SourceMemberStatus.DefaultConsumers?.ToArray() ?? Array.Empty<string>())
            .State(ProvenanceSettingState.Configured));
    }
}
