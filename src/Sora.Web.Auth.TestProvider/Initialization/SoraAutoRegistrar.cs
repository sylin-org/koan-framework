using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Web.Auth.Providers;
using Sora.Web.Auth.TestProvider.Infrastructure;
using Sora.Web.Auth.TestProvider.Options;

namespace Sora.Web.Auth.TestProvider.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Auth.TestProvider";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<DevTokenStore>();
    services.AddSoraOptions<TestProviderOptions>(TestProviderOptions.SectionPath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthProviderContributor, TestProviderContributor>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var enabled = env.IsDevelopment() || cfg.GetSection(TestProviderOptions.SectionPath).GetValue<bool>(nameof(TestProviderOptions.Enabled));
        report.AddSetting("Enabled", enabled ? "true" : "false");
        if (!env.IsDevelopment() && enabled)
        {
            report.AddNote("WARNING: TestProvider is enabled outside Development. Do not use in Production.");
        }
    }
}

internal sealed class TestProviderContributor(IConfiguration cfg, IHostEnvironment env) : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, Sora.Web.Auth.Options.ProviderOptions> GetDefaults()
    {
        var o = cfg.GetSection(TestProviderOptions.SectionPath).Get<TestProviderOptions>() ?? new TestProviderOptions();
        var enabled = env.IsDevelopment() || o.Enabled || o.ExposeInDiscoveryOutsideDevelopment;
        if (!enabled) return new Dictionary<string, Sora.Web.Auth.Options.ProviderOptions>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, Sora.Web.Auth.Options.ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["test"] = new Sora.Web.Auth.Options.ProviderOptions
            {
                Type = Sora.Web.Auth.Infrastructure.AuthConstants.Protocols.OAuth2,
                DisplayName = "Test (Local)",
                Icon = "/icons/test.svg",
                AuthorizationEndpoint = "/.testoauth/authorize",
                TokenEndpoint = "/.testoauth/token",
                UserInfoEndpoint = "/.testoauth/userinfo",
                ClientId = o.ClientId,
                ClientSecret = o.ClientSecret,
                Scopes = new []{ "identify", "email" },
                Enabled = true
            }
        };
    }
}
