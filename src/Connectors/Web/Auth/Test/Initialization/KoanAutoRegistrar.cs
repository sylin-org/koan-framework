using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Web.Auth.Providers;
using Koan.Web.Auth.Connector.Test.Infrastructure;
using Koan.Web.Auth.Connector.Test.Options;
using Koan.Web.Auth.Connector.Test.Controllers;
using Koan.Web.Extensions;
using TestProviderItems = Koan.Web.Auth.Connector.Test.Infrastructure.TestProviderProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Web.Auth.Connector.Test.Initialization;

// CORE-0091: This registrar contributes KoanTestProviderStartupFilter,
// which calls app.UseEndpoints(MapKoanTestProviderEndpoints) — that call
// requires UseRouting to be in the pipeline first. Koan.Web's startup
// filter is what puts UseRouting there, so this registrar's Initialize
// must run AFTER Koan.Web's (and transitively Koan.Web.Auth's). Before
// CORE-0091 the ordering came from ConcurrentDictionary enumeration
// luck; under-loaded apps would get the wrong order and the test
// provider's routes would silently never bind, falling through to the
// SPA's 404. Declaring the dependency explicitly makes the contract
// permanent.
[After(typeof(Koan.Web.Auth.Initialization.KoanAutoRegistrar))]
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Connector.Test";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<DevTokenStore>();
        services.AddKoanOptions<TestProviderOptions>(TestProviderOptions.SectionPath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, TestProviderContributor>());

        // Ensure MVC discovers TestProvider controllers
        services.AddKoanControllersFrom<StaticController>();
        // Map TestProvider endpoints during startup (honors RouteBase)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, Hosting.KoanTestProviderStartupFilter>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var enabledOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.Enabled)}",
            false);
        // SEC-0001 2h: opt-in only — no Development auto-enable (everyday dev login is the zero-config trust identity).
        var enabled = enabledOption.Value;

        module.PublishConfigValue(
            TestProviderItems.Enabled,
            enabledOption,
            displayOverride: enabled ? "true" : "false",
            modeOverride: ProvenanceModes.FromConfigurationValue(enabledOption),
            usedDefaultOverride: null,
            sourceKeyOverride: enabledOption.ResolvedKey);

        var routeBaseOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.RouteBase)}",
            "/.testoauth");
        var routeBase = string.IsNullOrWhiteSpace(routeBaseOption.Value)
            ? "/.testoauth"
            : routeBaseOption.Value.Trim();
        if (string.IsNullOrEmpty(routeBase))
        {
            routeBase = "/.testoauth";
        }
        if (!routeBase.StartsWith('/'))
        {
            routeBase = "/" + routeBase;
        }
        routeBase = routeBase.TrimEnd('/');

        var useJwtTokensOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.UseJwtTokens)}",
            false);
        var tokenFormat = useJwtTokensOption.Value ? "JWT" : "Hash";

        module.PublishConfigValue(
            TestProviderItems.TokenFormat,
            useJwtTokensOption,
            displayOverride: tokenFormat,
            sourceKeyOverride: useJwtTokensOption.ResolvedKey);

        if (useJwtTokensOption.Value)
        {
            var issuerOption = Koan.Core.Configuration.ReadWithSource(
                cfg,
                $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.JwtIssuer)}",
                "koan-test-provider");
            var audienceOption = Koan.Core.Configuration.ReadWithSource(
                cfg,
                $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.JwtAudience)}",
                "koan-test-client");
            var expirationOption = Koan.Core.Configuration.ReadWithSource(
                cfg,
                $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.JwtExpirationMinutes)}",
                60);

            module.PublishConfigValue(TestProviderItems.JwtIssuer, issuerOption);
            module.PublishConfigValue(TestProviderItems.JwtAudience, audienceOption);
            module.PublishConfigValue(TestProviderItems.JwtExpirationMinutes, expirationOption, displayOverride: $"{expirationOption.Value}min");
        }

        var clientCredentialsOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.EnableClientCredentials)}",
            false);

        module.PublishConfigValue(
            TestProviderItems.ClientCredentials,
            clientCredentialsOption,
            displayOverride: clientCredentialsOption.Value ? "Enabled" : "Disabled",
            sourceKeyOverride: clientCredentialsOption.ResolvedKey);

        var clientsSection = cfg.GetSection(TestProviderOptions.SectionPath)
            .GetSection(nameof(TestProviderOptions.RegisteredClients));
        var clientCount = clientsSection.GetChildren().Count();
        var registeredClientsValue = new ConfigurationValue<int>(
            clientCount,
            clientCount > 0 ? BootSettingSource.AppSettings : BootSettingSource.Auto,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.RegisteredClients)}",
            clientCount == 0);

        module.PublishConfigValue(
            TestProviderItems.RegisteredClients,
            registeredClientsValue,
            displayOverride: clientCount.ToString());

        module.AddNote(enabled
            ? "TestProvider ENABLED — opt-in OAuth-flow simulator (everyday dev login is the zero-config trust identity)."
            : $"TestProvider disabled (opt-in flow simulator). Set {TestProviderOptions.SectionPath}:Enabled=true to exercise the simulated OAuth flow.");

        module.AddTool(
            "Test Provider Login",
            $"{routeBase}/login.html",
            "Simulated OAuth login surface",
            capability: "auth.providers.test");
    }
}

