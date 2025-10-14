using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Web.Auth.Providers;
using Koan.Web.Auth.Connector.Test.Infrastructure;
using Koan.Web.Auth.Connector.Test.Options;
using Koan.Web.Auth.Connector.Test.Controllers;
using Koan.Web.Extensions;

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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var enabledOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.Enabled)}",
            false);
        var enabled = env.IsDevelopment() || enabledOption.Value;
        var enabledSource = enabledOption.Value
            ? enabledOption.Source
            : env.IsDevelopment()
                ? BootSettingSource.Environment
                : BootSettingSource.Auto;

        report.AddSetting(
            "Enabled",
            enabled ? "true" : "false",
            source: enabledSource,
            consumers: new[] { "Koan.Web.Auth.Connector.Test.Hosting.KoanTestProviderStartupFilter" },
            sourceKey: enabledOption.ResolvedKey);

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

        report.AddSetting(
            "TokenFormat",
            tokenFormat,
            source: useJwtTokensOption.Source,
            consumers: new[] { "Koan.Web.Auth.Connector.Test.Infrastructure.JwtTokenService" },
            sourceKey: useJwtTokensOption.ResolvedKey);

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

            report.AddSetting(
                "JWT.Issuer",
                issuerOption.Value,
                source: issuerOption.Source,
                consumers: new[] { "Koan.Web.Auth.Connector.Test.Infrastructure.JwtTokenService" },
                sourceKey: issuerOption.ResolvedKey);
            report.AddSetting(
                "JWT.Audience",
                audienceOption.Value,
                source: audienceOption.Source,
                consumers: new[] { "Koan.Web.Auth.Connector.Test.Infrastructure.JwtTokenService" },
                sourceKey: audienceOption.ResolvedKey);
            report.AddSetting(
                "JWT.Expiration",
                $"{expirationOption.Value}min",
                source: expirationOption.Source,
                consumers: new[] { "Koan.Web.Auth.Connector.Test.Infrastructure.JwtTokenService" },
                sourceKey: expirationOption.ResolvedKey);
        }

        var clientCredentialsOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.EnableClientCredentials)}",
            false);

        report.AddSetting(
            "ClientCredentials",
            clientCredentialsOption.Value ? "Enabled" : "Disabled",
            source: clientCredentialsOption.Source,
            consumers: new[] { "Koan.Web.Auth.Connector.Test.Controllers.TokenController" },
            sourceKey: clientCredentialsOption.ResolvedKey);

        var clientsSection = cfg.GetSection(TestProviderOptions.SectionPath)
            .GetSection(nameof(TestProviderOptions.RegisteredClients));
        var clientCount = clientsSection.GetChildren().Count();
        var clientsSource = clientCount > 0 ? BootSettingSource.AppSettings : BootSettingSource.Auto;

        report.AddSetting(
            "RegisteredClients",
            clientCount.ToString(),
            source: clientsSource,
            consumers: new[] { "Koan.Web.Auth.Connector.Test.Controllers.TokenController" },
            sourceKey: $"{TestProviderOptions.SectionPath}:{nameof(TestProviderOptions.RegisteredClients)}");

        if (!env.IsDevelopment() && enabled)
        {
            report.AddNote("WARNING: TestProvider is enabled outside Development. Do not use in Production.");
        }

        report.AddTool(
            "Test Provider Login",
            $"{routeBase}/login.html",
            "Simulated OAuth login surface",
            capability: "auth.providers.test");
    }
}
