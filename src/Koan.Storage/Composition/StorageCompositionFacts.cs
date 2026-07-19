using Koan.Core.Composition;
using Koan.Storage.Infrastructure;
using Koan.Storage.Options;
using Koan.Storage.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Storage.Composition;

internal static class StorageCompositionFacts
{
    public static void Project(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        var providers = services.GetService<StorageProviderCatalog>();
        var options = services.GetService<IOptions<StorageOptions>>()?.Value;
        if (providers is null || options is null) return;

        builder.AddConfigKey($"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.Profiles}");
        builder.AddConfigKey($"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.DefaultProfile}");

        foreach (var provider in providers.Candidates)
        {
            builder.AddCapability(
                $"storage:provider:{provider.Id}",
                provider.Capabilities.All.Select(static capability => capability.Id));
            builder.AddObservation(
                "koan.storage.provider.available",
                $"storage:provider:{provider.Id}",
                $"Storage provider '{provider.Id}' is available as {provider.Placement} with priority {provider.Priority}.",
                "provider-catalog",
                source);
        }

        if (!options.DeclaresRoutingIntent)
        {
            builder.AddObservation(
                "koan.storage.routing.available",
                "storage:routing",
                "Storage is available but inactive because no profile is configured; routing compiles when configuration or an actual Storage operation declares intent.",
                "no-routing-intent",
                source);
            return;
        }

        var routing = services.GetService<StorageRoutingPlan>();
        if (routing is null) return;

        foreach (var route in routing.Routes)
        {
            builder.AddElection(
                route.Receipt,
                source: source,
                factCode: "koan.storage.profile.selected");
            builder.AddCapability(
                $"storage:profile:{route.Name}",
                route.Capabilities.All.Select(static capability => capability.Id));
            builder.AddObservation(
                "koan.storage.profile.bounds",
                $"storage:profile:{route.Name}:bounds",
                $"Storage profile '{route.Name}' uses provider '{route.Provider.Name}' and container '{route.Container}'.",
                route.Receipt.Reason,
                source);
        }

        builder.AddObservation(
            "koan.storage.default.resolved",
            "storage:default-profile",
            routing.DefaultProfile is null
                ? "Storage has several profiles and no implicit default; callers must select one explicitly."
                : $"Storage default profile is '{routing.DefaultProfile}'.",
            routing.DefaultProfile is null ? "explicit-profile-required" : "compiled-default",
            source);
    }
}
