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
using Microsoft.Extensions.Logging;

namespace Koan.AI.Infrastructure;

/// <summary>
/// Projects the adapter and source decisions compiled by <c>AiModule.Start</c> into live provenance.
/// </summary>
internal sealed class AiProvenancePublisher(
    IAiAdapterRegistry adapters,
    IAiSourceRegistry sources,
    ILogger<AiProvenancePublisher>? logger = null)
{
    public async Task Publish(CancellationToken cancellationToken)
    {
        try
        {
            await PublishSnapshot(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to publish AI provenance snapshot");
        }
    }

    private async Task PublishSnapshot(CancellationToken ct)
    {
        var registry = ProvenanceRegistry.Instance;
        var module = registry.GetOrCreateModule(AiPillarManifest.PillarCode, "Koan.AI");

        await PublishAdapterRoster(module, ct).ConfigureAwait(false);
        PublishSourceHealth(module);
    }

    private Task PublishAdapterRoster(ProvenanceModuleWriter module, CancellationToken ct)
    {
        var summaries = new List<string>();

        foreach (var adapter in adapters.All)
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
            .Consumers(KoanAiProvenanceItems.AdapterRoster.DefaultConsumers?.ToArray() ?? [])
            .State(ProvenanceSettingState.Configured));

        return Task.CompletedTask;
    }

    private void PublishSourceHealth(ProvenanceModuleWriter module)
    {
        var allSources = sources.GetAllSources();
        if (allSources.Count == 0)
        {
            module.SetSetting(KoanAiProvenanceItems.SourceMemberStatus.Key, setting => setting
                .Label(KoanAiProvenanceItems.SourceMemberStatus.Label)
                .Description(KoanAiProvenanceItems.SourceMemberStatus.Description)
                .Value("(no sources registered)")
                .Source(ProvenanceSettingSource.Custom)
                .Consumers(KoanAiProvenanceItems.SourceMemberStatus.DefaultConsumers?.ToArray() ?? [])
                .State(ProvenanceSettingState.Default));
            return;
        }

        var details = new List<string>(allSources.Count);
        foreach (var source in allSources.OrderByDescending(s => s.Priority))
        {
            var memberSummaries = source.Members.Select(member =>
            {
                var health = member.HealthState.ToString();
                return $"{member.Name} [{health}]";
            });

            var healthState = source.GetHealthState();
            details.Add($"{source.Name} (enabled={source.IsEnabled.ToString().ToLowerInvariant()}, priority={source.Priority}, policy={source.Policy}, health={healthState}): {string.Join(", ", memberSummaries)}");
        }

        module.SetSetting(KoanAiProvenanceItems.SourceMemberStatus.Key, setting => setting
            .Label(KoanAiProvenanceItems.SourceMemberStatus.Label)
            .Description(KoanAiProvenanceItems.SourceMemberStatus.Description)
            .Value(string.Join("; ", details))
            .Source(ProvenanceSettingSource.Custom)
            .Consumers(KoanAiProvenanceItems.SourceMemberStatus.DefaultConsumers?.ToArray() ?? [])
            .State(ProvenanceSettingState.Configured));
    }
}
