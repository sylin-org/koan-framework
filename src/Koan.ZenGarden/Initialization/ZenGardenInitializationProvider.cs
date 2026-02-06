using Koan.ZenGarden.Core;
using Koan.ZenGarden.Models;
using Microsoft.Extensions.Logging;

namespace Koan.ZenGarden.Initialization;

internal sealed class ZenGardenInitializationProvider : IZenGardenInitializationProvider
{
    private readonly IZenGardenClient _client;
    private readonly ILogger<ZenGardenInitializationProvider> _logger;
    private readonly IReadOnlyDictionary<string, string> _offeringByAdapter;

    public ZenGardenInitializationProvider(
        IZenGardenClient client,
        IEnumerable<IZenGardenOfferingBinding> bindings,
        ILogger<ZenGardenInitializationProvider> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings ?? Array.Empty<IZenGardenOfferingBinding>())
        {
            if (string.IsNullOrWhiteSpace(binding.AdapterId) || string.IsNullOrWhiteSpace(binding.Offering))
            {
                continue;
            }

            map[binding.AdapterId.Trim().ToLowerInvariant()] = binding.Offering.Trim().ToLowerInvariant();
        }

        _offeringByAdapter = map;
    }

    public bool TryGetDefaultOffering(string adapterId, out string offering)
    {
        offering = string.Empty;
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            return false;
        }

        return _offeringByAdapter.TryGetValue(adapterId.Trim().ToLowerInvariant(), out offering!);
    }

    public async ValueTask<ZenGardenOfferingResolution?> ResolveAsync(
        ZenGardenConnectionIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var selector = intent.ToOfferingSelector();
        var subscription = ZenGardenSubscription.ForOffering(selector);
        if (intent.Capabilities.Count > 0)
        {
            subscription = subscription.Require(intent.Capabilities.ToArray());
        }

        var snapshot = await ResolveReadySnapshotAsync(subscription, cancellationToken).ConfigureAwait(false);
        if (snapshot is null && string.IsNullOrWhiteSpace(intent.Instance))
        {
            snapshot = await ResolveReadyInstanceSnapshotAsync(intent, subscription, cancellationToken).ConfigureAwait(false);
        }

        if (snapshot is null)
        {
            _logger.LogDebug(
                "Zen Garden resolution returned no ready offering for {Selector} (caps={Capabilities})",
                selector,
                intent.Capabilities.Count == 0 ? "(none)" : string.Join(",", intent.Capabilities));
            return null;
        }

        return MapResolution(snapshot);
    }

    private async Task<ZenGardenToolSnapshot?> ResolveReadySnapshotAsync(
        ZenGardenSubscription subscription,
        CancellationToken cancellationToken)
    {
        var tools = await CatalogAsync(subscription, cancellationToken).ConfigureAwait(false);
        return tools.FirstOrDefault(tool => tool.Ready);
    }

    private async Task<ZenGardenToolSnapshot?> ResolveReadyInstanceSnapshotAsync(
        ZenGardenConnectionIntent intent,
        ZenGardenSubscription scopedSubscription,
        CancellationToken cancellationToken)
    {
        var broadSubscription = new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.Offering,
            Requires = scopedSubscription.Requires
        };

        var tools = await CatalogAsync(broadSubscription, cancellationToken).ConfigureAwait(false);
        if (tools.Count == 0)
        {
            return null;
        }

        var exactFqid = $"offering:{intent.Offering}";
        var prefix = $"{exactFqid}:";

        var matched = tools
            .Where(tool => tool.Ready)
            .Where(tool =>
                string.Equals(tool.ToolFqid, exactFqid, StringComparison.OrdinalIgnoreCase) ||
                tool.ToolFqid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(scopedSubscription.RequirementsSatisfiedBy)
            .OrderBy(tool => string.Equals(tool.ToolFqid, exactFqid, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(tool => tool.ToolFqid, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (matched is not null)
        {
            _logger.LogDebug(
                "Zen Garden resolution matched instance candidate {ToolFqid} for offering {Offering}",
                matched.ToolFqid,
                intent.Offering);
        }

        return matched;
    }

    private async Task<IReadOnlyList<ZenGardenToolSnapshot>> CatalogAsync(
        ZenGardenSubscription subscription,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ZenGardenToolSnapshot> tools;
        try
        {
            tools = await _client.CatalogAsync(subscription, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Zen Garden catalog lookup failed for {ToolFqid}", subscription.ToolFqid);
            return Array.Empty<ZenGardenToolSnapshot>();
        }

        return tools;
    }

    private static ZenGardenOfferingResolution MapResolution(ZenGardenToolSnapshot snapshot)
    {
        var selector = snapshot.ToolFqid.StartsWith("offering:", StringComparison.OrdinalIgnoreCase)
            ? snapshot.ToolFqid["offering:".Length..]
            : snapshot.ToolFqid;

        var instanceSeparator = selector.IndexOf(':', StringComparison.Ordinal);
        var offering = instanceSeparator >= 0 ? selector[..instanceSeparator] : selector;
        var instance = instanceSeparator >= 0 ? selector[(instanceSeparator + 1)..] : null;

        return new ZenGardenOfferingResolution
        {
            ToolFqid = snapshot.ToolFqid,
            Offering = offering,
            Instance = string.IsNullOrWhiteSpace(instance) ? null : instance,
            Protocol = snapshot.Connection?.Protocol,
            Hostname = snapshot.Connection?.Hostname,
            Ip = snapshot.Connection?.Ip,
            Port = snapshot.Connection?.Port,
            Uris = snapshot.Connection?.Uris ?? Array.Empty<string>(),
            Capabilities = snapshot.Capabilities
        };
    }
}
