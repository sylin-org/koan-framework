using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Admin.Tests;

internal sealed class AdminWebHost : IAsyncDisposable
{
    private readonly IHost _host;

    private AdminWebHost(IHost host)
    {
        _host = host;
        Client = host.GetTestClient();
        Client.BaseAddress = new Uri("http://localhost");
    }

    public HttpClient Client { get; }

    public static async Task<AdminWebHost> StartAsync(
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<Microsoft.AspNetCore.Authorization.AuthorizationOptions>? configureAuthorization = null)
    {
        AppHost.Current = null;
        ProvenanceRegistry.Instance
            .GetOrCreateModule("core", "Koan.Admin.SecretFixture")
            .AddSetting("FixtureSecret", "never-project-this", isSecret: true, source: BootSettingSource.Custom);

        var configuration = new Dictionary<string, string?>
        {
            ["Koan:Environment"] = environment,
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning"
        };
        if (settings is not null)
        {
            foreach (var pair in settings)
            {
                configuration[pair.Key] = pair.Value;
            }
        }

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseContentRoot(AppContext.BaseDirectory);
                web.UseEnvironment(environment);
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(configuration));
                web.ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                            options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                            options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                            TestAuthenticationHandler.SchemeName,
                            _ => { });

                    if (configureAuthorization is not null)
                    {
                        services.AddAuthorization(configureAuthorization);
                    }
                });
                web.Configure(_ => { });
            });

        var host = builder.Build();
        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }

            throw;
        }

        return new AdminWebHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
