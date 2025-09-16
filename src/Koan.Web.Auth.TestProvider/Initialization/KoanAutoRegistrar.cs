using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Web.Auth.Providers;
using Koan.Web.Auth.TestProvider.Infrastructure;
using Koan.Web.Auth.TestProvider.Options;
using Koan.Web.Auth.TestProvider.Controllers;

namespace Koan.Web.Auth.TestProvider.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.TestProvider";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<DevTokenStore>();
        services.AddKoanOptions<TestProviderOptions>(TestProviderOptions.SectionPath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, TestProviderContributor>());

        // Configure TestProvider as default challenge scheme for development
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultChallengeScheme = "Test";
        });

        // Ensure MVC discovers TestProvider controllers
        services.AddControllers().AddApplicationPart(typeof(StaticController).Assembly);
        // Map TestProvider endpoints during startup (honors RouteBase)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, Hosting.KoanTestProviderStartupFilter>());
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var enabled = env.IsDevelopment() || cfg.GetSection(TestProviderOptions.SectionPath).GetValue<bool>(nameof(TestProviderOptions.Enabled));
        report.AddSetting("Enabled", enabled ? "true" : "false");

        if (enabled)
        {
            var section = cfg.GetSection(TestProviderOptions.SectionPath);
            var useJwtTokens = section.GetValue<bool>(nameof(TestProviderOptions.UseJwtTokens));
            var tokenFormat = useJwtTokens ? "JWT" : "Hash";
            var issuer = section.GetValue<string>(nameof(TestProviderOptions.JwtIssuer)) ?? "koan-test-provider";
            var audience = section.GetValue<string>(nameof(TestProviderOptions.JwtAudience)) ?? "koan-test-client";
            var expiration = section.GetValue<int>(nameof(TestProviderOptions.JwtExpirationMinutes));

            report.AddSetting("TokenFormat", tokenFormat);
            if (useJwtTokens)
            {
                report.AddSetting("JWT.Issuer", issuer);
                report.AddSetting("JWT.Audience", audience);
                report.AddSetting("JWT.Expiration", $"{expiration}min");
            }
        }

        if (!env.IsDevelopment() && enabled)
        {
            report.AddNote("WARNING: TestProvider is enabled outside Development. Do not use in Production.");
        }
    }
}