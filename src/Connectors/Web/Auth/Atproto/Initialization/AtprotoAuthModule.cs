using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto.Controllers;
using Koan.Web.Auth.Connector.Atproto.Hosting;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Auth.Connector.Atproto.Options;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Connector.Atproto.Storage;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Connector.Atproto.Initialization;

// WEB-0081: Start decides the client-identity check composition deferred, so it runs after Koan.Web has read the
// application's accepted URL.
[After(typeof(Koan.Web.Initialization.WebModule))]
public sealed class AtprotoAuthModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<AtprotoOptions>(Constants.Section);
        services.AddSingleton<IAuthProtocol, AtprotoProtocol>();
        services.AddSingleton(new AuthProviderDefinition(Constants.Provider, new ProviderOptions
        { Type = Constants.Protocol, DisplayName = "AT Protocol", Scopes = [Constants.BaseScope], Priority = 200 }));
        services.AddSingleton<AtprotoHttp>();
        services.AddSingleton(sp => new AtprotoSessionStore(sp.GetRequiredService<IDataProtectionProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AtprotoSessionStore>>()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<AtprotoSessionSweep>();
        services.AddSingleton(sp => new AtprotoSessions(sp.GetRequiredService<AtprotoSessionStore>(), sp.GetRequiredService<AtprotoHttp>(),
            sp.GetRequiredService<IOptions<AtprotoOptions>>(), sp.GetRequiredService<IOptions<AuthOptions>>(),
            sp.GetRequiredService<IAuthProviderCatalog>(), sp.GetRequiredService<IHostEnvironment>(), sp.GetRequiredService<KoanWebBaseUrl>(),
            sp.GetService<IHttpContextAccessor>()));
        services.AddKoanControllersFrom<AtprotoMetadataController>();
        services.AddAuthentication().AddScheme<AtprotoAuthenticationOptions, AtprotoAuthenticationHandler>(Constants.Provider, options =>
        {
            options.SignInScheme = AuthenticationExtensions.CookieScheme;
            options.CallbackPath = Constants.Callback;
            // WEB-0078: no explicit Path, so the correlation cookie is scoped to the request's path base plus the
            // callback path, which is where the callback lands under Koan:Web:ApplicationUrl.
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.HttpOnly = true;
            options.Events = new RemoteAuthenticationEvents
            {
                // WEB-0080: the handler sends a failed callback to the application's landing itself; this answers the
                // failures the base handler raises, the same way when a landing is named, else as before.
                OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    if (SignInFailureReturn.Redirect(context.HttpContext, context.Properties, SignInFailure.Failed)) return Task.CompletedTask;
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.Headers.CacheControl = "no-store";
                    return context.Response.WriteAsJsonAsync(new { error = "atproto_authentication_failed" });
                }
            };
        });
        services.AddOptions<AtprotoAuthenticationOptions>(Constants.Provider)
            .Configure<IDataProtectionProvider, IHostEnvironment>((options, protection, environment) =>
            {
                options.StateDataFormat = new PropertiesDataFormat(protection.CreateProtector(Constants.PropertiesPurpose));
                options.CorrelationCookie.SecurePolicy = KoanEnv.Gate.DevelopmentOnly(environment) ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            });
    }

    /// <summary>
    /// WEB-0081: with nothing configured and an application store, composition cannot see the accepted URL the client
    /// identity derives from. Once Koan.Web has read the store, an eligible provider without an identity it can publish
    /// refuses the start with the correction composition would have given. An eligible provider also refuses to start on
    /// a store that cannot replace a session row conditionally, since every session write depends on it.
    /// </summary>
    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var provider = services.GetRequiredService<IAuthProviderCatalog>().Find(Constants.Provider);
        if (provider?.Eligible != true) return Task.CompletedTask;
        var clientId = services.GetRequiredService<IOptions<AuthOptions>>().Value.Providers.GetValueOrDefault(Constants.Provider)?.ClientId;
        services.GetServices<IAuthProtocol>().OfType<AtprotoProtocol>().Single().EnsureClientIdentity(clientId, provider.Scopes.ToArray());
        using var host = AppHost.PushScope(services);
        if (!Data<AtprotoSession, string>.Capabilities.Has(DataCaps.Write.ConditionalReplace))
            throw new InvalidOperationException(
                $"Koan Web Auth provider '{Constants.Provider}' keeps each AT session as a row that changes only by a conditional write, " +
                $"and the store elected for {nameof(AtprotoSession)} cannot replace a row conditionally. Reference a data connector that can " +
                "(SQLite, PostgreSQL, SQL Server and MongoDB do), or route the entity to one.");
        return Task.CompletedTask;
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Native AT Protocol public-client OAuth; verified DID identity, guarded discovery, sessions as protected rows in the application's store, shared by every process over it. Web Auth owns provider eligibility and browser cookie lifecycle.");
    }
}
