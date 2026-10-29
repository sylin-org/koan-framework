using Koan.Core.Composition;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Composition;

internal static class AuthCompositionFacts
{
    public static void Project(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        var plan = services.GetService<AuthProviderPlan>();
        if (plan is null) return;

        builder.AddConfigKey($"{AuthOptions.SectionPath}:{nameof(AuthOptions.Providers)}");
        builder.AddConfigKey($"{AuthOptions.SectionPath}:{nameof(AuthOptions.PreferredProviderId)}");
        builder.AddConfigKey($"{AuthOptions.SectionPath}:{nameof(AuthOptions.ResourceServer)}");

        // SEC-0025: whether ordinary requests accept this application's own bearer, and for which resource.
        if (services.GetService<IOptions<AuthOptions>>()?.Value.ResourceServer is { Enabled: true } resourceServer)
        {
            builder.AddObservation(
                "koan.auth.resourceserver.enabled",
                "auth:resource-server",
                "A request that carries an Authorization header is authenticated by its bearer alone, accepted only when the token's audience is " +
                (resourceServer.Resource ?? "the application's base URL") +
                "; its subject is the person's identifier, and a token not accepted answers 401 with Bearer error=\"invalid_token\". " +
                "Every other request keeps the session cookie.",
                resourceServer.Resource is null ? "base-url" : "configured-resource",
                source);
        }

        foreach (var provider in plan.Providers)
        {
            builder.AddObservation(
                $"koan.auth.provider.{provider.State}",
                $"auth:provider:{provider.Id}",
                $"Authentication provider '{provider.Id}' is {provider.State}: {provider.Reason}." +
                (provider.Correction is null ? string.Empty : $" Correction: {provider.Correction}"),
                provider.Reason,
                source);
        }

        if (plan.Receipt is not null)
        {
            builder.AddElection(
                plan.Receipt,
                source: source,
                factCode: "koan.auth.provider.selected");
        }
        else
        {
            builder.AddObservation(
                "koan.auth.provider.none",
                "auth:provider:default",
                "No eligible interactive authentication provider is configured; cookie authentication remains available without an external challenge target.",
                "no-eligible-provider",
                source);
        }
    }
}
