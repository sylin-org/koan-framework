using Koan.Core.Composition;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Auth.Composition;

internal static class AuthCompositionFacts
{
    public static void Project(KoanCompositionBuilder builder, IServiceProvider services, string source)
    {
        var plan = services.GetService<AuthProviderPlan>();
        if (plan is null) return;

        builder.AddConfigKey($"{AuthOptions.SectionPath}:{nameof(AuthOptions.Providers)}");
        builder.AddConfigKey($"{AuthOptions.SectionPath}:{nameof(AuthOptions.PreferredProviderId)}");

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
