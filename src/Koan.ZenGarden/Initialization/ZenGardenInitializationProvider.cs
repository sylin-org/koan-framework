using Koan.ZenGarden.Core;
using Koan.ZenGarden.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Koan.ZenGarden.Initialization;

internal sealed class ZenGardenInitializationProvider : IZenGardenInitializationProvider
{
    private readonly IZenGardenClient _client;
    private readonly ILogger<ZenGardenInitializationProvider> _logger;
    private readonly IReadOnlyDictionary<string, string> _offeringByAdapter;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _wishScheduleCache = new(StringComparer.OrdinalIgnoreCase);

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

    public async ValueTask<ZenGardenCapabilityWishReceipt?> WishCapabilities(
        ZenGardenConnectionIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Capabilities.Count == 0)
        {
            return null;
        }

        ZenGardenCapabilityWish wish;
        try
        {
            wish = await _client.Wish(
                intent.ToOfferingSelector(),
                intent.Capabilities,
                options: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Zen Garden capability wish failed for {Selector} (caps={Capabilities})",
                intent.ToOfferingSelector(),
                string.Join(",", intent.Capabilities));
            return null;
        }

        return new ZenGardenCapabilityWishReceipt
        {
            RequestId = wish.RequestId,
            ToolFqid = wish.ToolFqid,
            OfferingSelector = wish.OfferingSelector,
            Requested = wish.Requested,
            Missing = wish.Missing,
            IsFulfilled = wish.IsFulfilled,
            Status = wish.Status,
            CreatedAt = wish.CreatedAt
        };
    }

    public async ValueTask<ZenGardenOfferingResolution?> Resolve(
        ZenGardenConnectionIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var selector = intent.ToOfferingSelector();
        var subscription = ZenGardenSubscription.ForOffering(selector);

        var snapshot = await ResolveReadySnapshot(subscription, cancellationToken).ConfigureAwait(false);
        if (snapshot is null && string.IsNullOrWhiteSpace(intent.Instance))
        {
            snapshot = await ResolveReadyInstanceSnapshot(intent, cancellationToken).ConfigureAwait(false);
        }

        if (snapshot is null)
        {
            _logger.LogDebug(
                "Zen Garden resolution returned no ready offering for {Selector} (caps={Capabilities})",
                selector,
                intent.Capabilities.Count == 0 ? "(none)" : string.Join(",", intent.Capabilities));
            return null;
        }

        if (intent.Capabilities.Count > 0)
        {
            await EnsureCapabilitiesWishfully(intent, snapshot, cancellationToken).ConfigureAwait(false);
        }

        return MapResolution(snapshot);
    }

    private async Task<ZenGardenToolSnapshot?> ResolveReadySnapshot(
        ZenGardenSubscription subscription,
        CancellationToken cancellationToken)
    {
        var tools = await Catalog(subscription, cancellationToken).ConfigureAwait(false);
        return tools.FirstOrDefault(tool => tool.Ready);
    }

    private async Task<ZenGardenToolSnapshot?> ResolveReadyInstanceSnapshot(
        ZenGardenConnectionIntent intent,
        CancellationToken cancellationToken)
    {
        var requirements = intent.Capabilities.Count == 0
            ? Array.Empty<ZenGardenCapabilityRequirement>()
            : ZenGardenCapabilityRequirement.ParseMany(intent.Capabilities).ToArray();

        var broadSubscription = new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.Offering
        };

        var tools = await Catalog(broadSubscription, cancellationToken).ConfigureAwait(false);
        if (tools.Count == 0)
        {
            return null;
        }

        var query = Core.ToolFqid.Parse(intent.Offering);

        var matched = tools
            .Where(tool => tool.Ready)
            .Where(tool => query.MatchesSnapshot(tool.ToolFqid, tool.OfferingType, tool.Aliases))
            .OrderBy(tool => string.Equals(tool.ToolFqid, intent.Offering, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(tool => tool.Aliases.Any(alias => string.Equals(alias, intent.Offering, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
            .ThenBy(tool => requirements.Length > 0 && requirements.All(req => req.Matches(tool.Capabilities)) ? 0 : 1)
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

    private async Task<IReadOnlyList<ZenGardenToolSnapshot>> Catalog(
        ZenGardenSubscription subscription,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ZenGardenToolSnapshot> tools;
        try
        {
            tools = await _client.Catalog(subscription, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Zen Garden catalog lookup failed for {ToolFqid}", subscription.ToolFqid);
            return Array.Empty<ZenGardenToolSnapshot>();
        }

        return tools;
    }

    private async Task EnsureCapabilitiesWishfully(
        ZenGardenConnectionIntent intent,
        ZenGardenToolSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var requirements = ZenGardenCapabilityRequirement.ParseMany(intent.Capabilities).ToArray();
        if (requirements.Length == 0)
        {
            return;
        }

        var missing = requirements
            .Where(req => !req.Matches(snapshot.Capabilities))
            .Select(req => req.Canonical)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        var cacheKey = $"{intent.ToOfferingSelector()}|{string.Join(",", missing.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))}";
        var now = DateTimeOffset.UtcNow;
        if (_wishScheduleCache.TryGetValue(cacheKey, out var lastScheduled) &&
            now - lastScheduled < TimeSpan.FromSeconds(15))
        {
            _logger.LogDebug(
                "Zen Garden wish scheduling throttled for {Selector} (missing={Capabilities})",
                intent.ToOfferingSelector(),
                string.Join(",", missing));
            return;
        }

        _wishScheduleCache[cacheKey] = now;
        var receipt = await WishCapabilities(intent, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            _logger.LogDebug(
                "Zen Garden wish scheduling skipped/failed for {Selector} (missing={Capabilities})",
                intent.ToOfferingSelector(),
                string.Join(",", missing));
            return;
        }

        _logger.LogInformation(
            "Zen Garden wish scheduled for {Selector} request={RequestId} missing={Capabilities}",
            intent.ToOfferingSelector(),
            receipt.RequestId,
            string.Join(",", receipt.Missing));
    }

    private static ZenGardenOfferingResolution MapResolution(ZenGardenToolSnapshot snapshot)
    {
        var parsed = Core.ToolFqid.Parse(snapshot.ToolFqid);

        return new ZenGardenOfferingResolution
        {
            ToolFqid = snapshot.ToolFqid,
            Offering = parsed.OfferingType,
            Instance = parsed.Instance,
            Protocol = snapshot.Connection?.Protocol,
            Hostname = snapshot.Connection?.Hostname,
            Ip = snapshot.Connection?.Ip,
            Port = snapshot.Connection?.Port,
            Uris = snapshot.Connection?.Uris ?? Array.Empty<string>(),
            Capabilities = snapshot.Capabilities
        };
    }
}
