using Koan.Core.Providers;
using Koan.Storage.Abstractions;
using Koan.Storage.Options;
using Koan.Storage.Replication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Storage.Routing;

/// <summary>Immutable profile routes compiled once for one host.</summary>
internal sealed class StorageRoutingPlan : IDisposable
{
    private readonly IReadOnlyDictionary<string, StorageRoute> _routes;
    private readonly IReadOnlyList<ReplicatedStorageProvider> _composites;

    public StorageRoutingPlan(
        IOptions<StorageOptions> options,
        StorageProviderCatalog providers,
        ILogger<StorageRoutingPlan> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        var configured = options.Value;
        if (configured.Profiles is null || configured.Profiles.Count == 0)
        {
            throw new InvalidOperationException(
                "Koan Storage has no profiles. Configure at least one Koan:Storage:Profiles entry with a container.");
        }

        var routes = new Dictionary<string, StorageRoute>(StringComparer.OrdinalIgnoreCase);
        var composites = new List<ReplicatedStorageProvider>();
        try
        {
            foreach (var (name, profile) in configured.Profiles.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException("Koan Storage profile names cannot be empty.");
                if (string.IsNullOrWhiteSpace(profile.Container))
                    throw new InvalidOperationException($"Koan Storage profile '{name}' has no container.");

                routes.Add(name, CompileRoute(name, profile, providers, composites, logger));
            }

            if (!string.IsNullOrWhiteSpace(configured.DefaultProfile))
            {
                if (!routes.ContainsKey(configured.DefaultProfile))
                {
                    throw new InvalidOperationException(
                        $"Koan Storage DefaultProfile '{configured.DefaultProfile}' does not exist. " +
                        $"Choose one of: {string.Join(", ", routes.Keys.Order(StringComparer.Ordinal))}.");
                }

                DefaultProfile = configured.DefaultProfile;
            }
            else if (routes.Count == 1)
            {
                DefaultProfile = routes.Keys.Single();
            }

            _routes = routes;
            _composites = composites;
            Routes = routes.Values.OrderBy(static route => route.Name, StringComparer.Ordinal).ToArray();
        }
        catch
        {
            foreach (var composite in composites)
            {
                try
                {
                    composite.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    logger.LogWarning(
                        cleanupFailure,
                        "Koan Storage could not dispose a partially compiled replicated route after startup failed.");
                }
            }

            throw;
        }
    }

    public string? DefaultProfile { get; }
    public IReadOnlyList<StorageRoute> Routes { get; }

    public (StorageRoute Route, string Container) Resolve(string? profile, string? container)
    {
        var profileName = string.IsNullOrWhiteSpace(profile) ? DefaultProfile : profile;
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new InvalidOperationException(
                "Koan Storage cannot infer a profile because several profiles are configured. " +
                "Set Koan:Storage:DefaultProfile or pass an explicit profile.");
        }

        if (!_routes.TryGetValue(profileName, out var route))
        {
            throw new InvalidOperationException(
                $"Unknown Koan Storage profile '{profileName}'. Available profiles: " +
                $"{string.Join(", ", _routes.Keys.Order(StringComparer.Ordinal))}.");
        }

        return (route, string.IsNullOrWhiteSpace(container) ? route.Container : container);
    }

    public void Dispose()
    {
        foreach (var composite in _composites) composite.Dispose();
    }

    private static StorageRoute CompileRoute(
        string name,
        StorageOptions.StorageProfile profile,
        StorageProviderCatalog providers,
        ICollection<ReplicatedStorageProvider> composites,
        ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(profile.Provider))
        {
            var exact = providers.Find(profile.Provider)
                ?? throw MissingProvider(name, profile.Provider, providers.Candidates);

            if (profile.Mode is StorageMode.Local or StorageMode.Remote)
            {
                var requiredPlacement = profile.Mode == StorageMode.Local
                    ? StorageProviderPlacement.Local
                    : StorageProviderPlacement.Remote;
                if (exact.Placement != requiredPlacement)
                {
                    throw new InvalidOperationException(
                        $"Koan Storage profile '{name}' pins provider '{exact.Id}' ({exact.Placement}) but requires " +
                        $"{requiredPlacement} mode. Correct the provider or remove the contradictory mode.");
                }
            }

            if (profile.Mode != StorageMode.Replicated)
                return Route(name, profile.Container, exact, ProviderIntentPosture.Required, "explicit-provider");

            if (exact.Placement == StorageProviderPlacement.Composite)
                return Route(name, profile.Container, exact, ProviderIntentPosture.Required, "explicit-composite");

            return ComposeReplicated(name, profile, exact, providers, composites, logger, explicitProvider: true);
        }

        return profile.Mode switch
        {
            StorageMode.Local => RequirePlacement(name, profile.Container, providers, StorageProviderPlacement.Local),
            StorageMode.Remote => RequirePlacement(name, profile.Container, providers, StorageProviderPlacement.Remote),
            StorageMode.Replicated => ComposeReplicated(name, profile, null, providers, composites, logger, explicitProvider: false),
            null => Automatic(name, profile, providers, composites, logger),
            _ => throw new InvalidOperationException(
                $"Koan Storage profile '{name}' has unsupported mode value '{profile.Mode}'.")
        };
    }

    private static StorageRoute Automatic(
        string name,
        StorageOptions.StorageProfile profile,
        StorageProviderCatalog providers,
        ICollection<ReplicatedStorageProvider> composites,
        ILogger logger)
    {
        var local = providers.Best(StorageProviderPlacement.Local);
        var remote = providers.Best(StorageProviderPlacement.Remote);
        if (local is not null && remote is not null)
            return Compose(
                name,
                profile,
                local,
                remote,
                composites,
                logger,
                ProviderIntentPosture.Automatic,
                "automatic-replicated");
        if (local is not null)
            return Route(name, profile.Container, local, ProviderIntentPosture.Automatic, "automatic-local");
        if (remote is not null)
            return Route(name, profile.Container, remote, ProviderIntentPosture.Automatic, "automatic-remote");

        throw new InvalidOperationException(
            $"Koan Storage profile '{name}' has no provider pin and no Local or Remote provider is referenced. " +
            "Reference a Storage connector or configure an exact Provider.");
    }

    private static StorageRoute RequirePlacement(
        string name,
        string container,
        StorageProviderCatalog providers,
        StorageProviderPlacement placement)
    {
        var selected = providers.Best(placement)
            ?? throw new InvalidOperationException(
                $"Koan Storage profile '{name}' requires {placement} mode, but no {placement} provider is referenced.");
        return Route(name, container, selected, ProviderIntentPosture.Required, $"required-{placement.ToString().ToLowerInvariant()}");
    }

    private static StorageRoute ComposeReplicated(
        string name,
        StorageOptions.StorageProfile profile,
        StorageProviderCandidate? exact,
        StorageProviderCatalog providers,
        ICollection<ReplicatedStorageProvider> composites,
        ILogger logger,
        bool explicitProvider)
    {
        var local = exact?.Placement == StorageProviderPlacement.Local
            ? exact
            : providers.Best(StorageProviderPlacement.Local);
        var remote = exact?.Placement == StorageProviderPlacement.Remote
            ? exact
            : providers.Best(StorageProviderPlacement.Remote);

        if (local is null || remote is null)
        {
            var missing = local is null ? "Local" : "Remote";
            throw new InvalidOperationException(
                $"Koan Storage profile '{name}' requires Replicated mode, but no {missing} provider is referenced. " +
                "Reference both placements or choose Local/Remote mode; Koan will not weaken replication intent.");
        }

        return Compose(
            name,
            profile,
            local,
            remote,
            composites,
            logger,
            ProviderIntentPosture.Required,
            explicitProvider ? "explicit-replicated" : "required-replicated");
    }

    private static StorageRoute Compose(
        string name,
        StorageOptions.StorageProfile profile,
        StorageProviderCandidate local,
        StorageProviderCandidate remote,
        ICollection<ReplicatedStorageProvider> composites,
        ILogger logger,
        ProviderIntentPosture posture,
        string reason)
    {
        var provider = new ReplicatedStorageProvider(
            local.Provider,
            remote.Provider,
            profile.Container,
            profile.LocalCache,
            logger);
        composites.Add(provider);
        var capabilities = Abstractions.Capabilities.StorageCaps.Describe(provider, provider.Name);
        return new StorageRoute(
            name,
            profile.Container,
            provider,
            capabilities,
            new ProviderSelectionReceipt(
                $"storage:profile:{name}",
                provider.Name,
                posture,
                Math.Max(local.Priority, remote.Priority),
                reason));
    }

    private static StorageRoute Route(
        string name,
        string container,
        StorageProviderCandidate provider,
        ProviderIntentPosture posture,
        string reason)
        => new(
            name,
            container,
            provider.Provider,
            provider.Capabilities,
            new ProviderSelectionReceipt(
                $"storage:profile:{name}",
                provider.Id,
                posture,
                provider.Priority,
                reason));

    private static InvalidOperationException MissingProvider(
        string profile,
        string provider,
        IReadOnlyList<StorageProviderCandidate> candidates)
        => new(
            $"Koan Storage profile '{profile}' references unknown provider '{provider}'. " +
            $"Referenced providers: {(candidates.Count == 0 ? "none" : string.Join(", ", candidates.Select(static candidate => candidate.Id)))}. " +
            "Correct the provider name or reference its connector.");
}

internal sealed record StorageRoute(
    string Name,
    string Container,
    IStorageProvider Provider,
    Koan.Core.Capabilities.CapabilitySet Capabilities,
    ProviderSelectionReceipt Receipt);
