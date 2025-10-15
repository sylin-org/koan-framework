using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Web.Auth.Providers;
using Koan.Web.Auth.Connector.Test.Infrastructure;
using Koan.Web.Auth.Connector.Test.Options;
using Koan.Web.Auth.Connector.Test.Controllers;
using Koan.Web.Extensions;
using TestProviderItems = Koan.Web.Auth.Connector.Test.Infrastructure.TestProviderProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Web.Auth.Connector.Test.Initialization;

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
        var enabled = env.IsDevelopment() || enabledOption.Value;
        var enabledMode = env.IsDevelopment() && !enabledOption.Value
            ? ProvenanceModes.FromBootSource(BootSettingSource.Environment, usedDefault: false)
            : ProvenanceModes.FromConfigurationValue(enabledOption);

        Publish(
            module,
            TestProviderItems.Enabled,
            enabledOption,
            displayOverride: enabled ? "true" : "false",
            modeOverride: enabledMode,
            usedDefaultOverride: env.IsDevelopment() && !enabledOption.Value ? false : null,
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

        Publish(
            module,
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

            Publish(module, TestProviderItems.JwtIssuer, issuerOption);
            Publish(module, TestProviderItems.JwtAudience, audienceOption);
            Publish(module, TestProviderItems.JwtExpirationMinutes, expirationOption, displayOverride: $"{expirationOption.Value}min");
        }

        var clientCredentialsOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.EnableClientCredentials)}",
            false);

        Publish(
            module,
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

        Publish(
            module,
            TestProviderItems.RegisteredClients,
            registeredClientsValue,
            displayOverride: clientCount.ToString());

        if (!env.IsDevelopment() && enabled)
        {
            module.AddNote("WARNING: TestProvider is enabled outside Development. Do not use in Production.");
        }

        module.AddTool(
            "Test Provider Login",
            $"{routeBase}/login.html",
            "Simulated OAuth login surface",
            capability: "auth.providers.test");
    }

    private static void Publish<T>(
        ProvenanceModuleWriter module,
        ProvenanceItem item,
        ConfigurationValue<T> value,
        object? displayOverride = null,
        ProvenancePublicationMode? modeOverride = null,
        bool? usedDefaultOverride = null,
        string? sourceKeyOverride = null,
        bool? sanitizeOverride = null)
    {
        module.AddSetting(
            item,
            modeOverride ?? ProvenanceModes.FromConfigurationValue(value),
            displayOverride ?? value.Value,
            sourceKey: sourceKeyOverride ?? value.ResolvedKey,
            usedDefault: usedDefaultOverride ?? value.UsedDefault,
            sanitizeOverride: sanitizeOverride);
    }
}

