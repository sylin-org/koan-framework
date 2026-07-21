using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Web.Auth.Connector.Test.Controllers;
using Koan.Web.Auth.Connector.Test.Infrastructure;
using Koan.Web.Auth.Connector.Test.Options;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Koan.Web.Extensions;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;
using TestProviderItems = Koan.Web.Auth.Connector.Test.Infrastructure.TestProviderProvenanceItems;

namespace Koan.Web.Auth.Connector.Test.Initialization;

/// <summary>
/// Adds the local OAuth2/OIDC simulator. Controllers own stable protocol routes; Web Auth owns provider
/// eligibility, election, scheme creation, and reporting through the two immutable definitions registered here.
/// </summary>
public sealed class TestAuthModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<DevTokenStore>();
        services.AddKoanOptions<TestProviderOptions>(TestProviderOptions.SectionPath);
        services.AddKoanControllersFrom<StaticController>();

        services.AddSingleton<AuthProviderDefinition>(services =>
            BuildDefinition(services, AuthProviderProtocols.OAuth2));
        services.AddSingleton<AuthProviderDefinition>(services =>
            BuildDefinition(services, AuthProviderProtocols.Oidc));
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var enabledOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.Enabled)}",
            false);
        var active = enabledOption.Value || env.IsDevelopment();

        module.PublishConfigValue(
            TestProviderItems.Enabled,
            enabledOption,
            displayOverride: active ? "active" : "inactive",
            modeOverride: ProvenanceModes.FromConfigurationValue(enabledOption),
            usedDefaultOverride: null,
            sourceKeyOverride: enabledOption.ResolvedKey);

        var useJwtTokensOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.UseJwtTokens)}",
            false);
        module.PublishConfigValue(
            TestProviderItems.TokenFormat,
            useJwtTokensOption,
            displayOverride: useJwtTokensOption.Value ? "JWT" : "Hash",
            sourceKeyOverride: useJwtTokensOption.ResolvedKey);

        if (useJwtTokensOption.Value)
        {
            module.PublishConfigValue(TestProviderItems.JwtIssuer, Koan.Core.Configuration.ReadWithSource(
                cfg, $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.JwtIssuer)}", "koan-test-provider"));
            module.PublishConfigValue(TestProviderItems.JwtAudience, Koan.Core.Configuration.ReadWithSource(
                cfg, $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.JwtAudience)}", "koan-test-client"));
            var expiration = Koan.Core.Configuration.ReadWithSource(
                cfg, $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.JwtExpirationMinutes)}", 60);
            module.PublishConfigValue(TestProviderItems.JwtExpirationMinutes, expiration, displayOverride: $"{expiration.Value}min");
        }

        var clientCredentials = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.EnableClientCredentials)}",
            false);
        module.PublishConfigValue(
            TestProviderItems.ClientCredentials,
            clientCredentials,
            displayOverride: clientCredentials.Value ? "Enabled" : "Disabled",
            sourceKeyOverride: clientCredentials.ResolvedKey);

        var clientCount = cfg.GetSection(TestProviderOptions.SectionPath)
            .GetSection(nameof(TestProviderOptions.RegisteredClients))
            .GetChildren()
            .Count();
        module.PublishConfigValue(
            TestProviderItems.RegisteredClients,
            new ConfigurationValue<int>(
                clientCount,
                clientCount > 0 ? BootSettingSource.AppSettings : BootSettingSource.Auto,
                $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.RegisteredClients)}",
                clientCount == 0),
            displayOverride: clientCount.ToString());

        module.AddNote(active
            ? "Local OAuth2/OIDC simulator available; Web Auth reports whether either provider is eligible or elected."
            : $"Local simulator inactive outside Development. Set {TestProviderOptions.SectionPath}:Enabled=true to enable it.");
        module.AddTool(
            "Test Provider Login",
            Constants.Routes.Login,
            "Simulated OAuth login surface",
            capability: "auth.providers.test");
    }

    private static AuthProviderDefinition BuildDefinition(IServiceProvider services, string protocol)
    {
        var options = services.GetRequiredService<IOptions<TestProviderOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();
        var active = options.IsActive(environment);
        var isOidc = protocol == AuthProviderProtocols.Oidc;

        return new AuthProviderDefinition(
            isOidc ? "test-oidc" : "test",
            new ProviderOptions
            {
                Type = protocol,
                DisplayName = isOidc ? "Test OIDC (Local)" : "Test OAuth2 (Local)",
                Icon = "/icons/test.svg",
                Authority = isOidc ? Constants.Routes.Base : null,
                AuthorizationEndpoint = isOidc ? null : Constants.Routes.Authorize,
                TokenEndpoint = isOidc ? null : Constants.Routes.Token,
                UserInfoEndpoint = isOidc ? null : Constants.Routes.UserInfo,
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
                Scopes = isOidc ? ["openid", "profile", "email"] : ["identify", "email"],
                Priority = isOidc ? 26 : 25
            },
            Automatic: active,
            Available: active,
            AvailabilityReason: active
                ? "local simulator available"
                : "local simulator is disabled outside Development");
    }
}
