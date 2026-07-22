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
using Microsoft.Extensions.Logging;
using System.Runtime.ExceptionServices;
using Xunit;

namespace Koan.Web.Admin.Tests;

internal sealed class AdminWebHost : IAsyncDisposable
{
    private readonly IHost _host;

    private AdminWebHost(IHost host, IServiceProvider ambientServices)
    {
        _host = host;
        AmbientServices = ambientServices;
        Client = host.GetTestClient();
        Client.BaseAddress = new Uri("http://localhost");
    }

    public HttpClient Client { get; }
    public IServiceProvider AmbientServices { get; }

    public static async Task<AdminWebHost> StartAsync(
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<Microsoft.AspNetCore.Authorization.AuthorizationOptions>? configureAuthorization = null)
    {
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
            .ConfigureLogging(logging => logging.ClearProviders())
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
        catch (Exception startFailure)
        {
            try
            {
                if (host is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
                else host.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Admin Web host startup and cleanup both failed.",
                    startFailure,
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(startFailure).Throw();
        }

        return new AdminWebHost(
            host,
            AppHost.Current ?? throw new InvalidOperationException("The Admin Web host did not attach its ambient provider."));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Exception? stopFailure = null;
        try
        {
            await _host.StopAsync();
        }
        catch (Exception exception)
        {
            stopFailure = exception;
        }

        try
        {
            if (_host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync();
            else _host.Dispose();
        }
        catch (Exception disposeFailure) when (stopFailure is not null)
        {
            throw new AggregateException("Admin Web host stop and disposal both failed.", stopFailure, disposeFailure);
        }

        if (stopFailure is not null) ExceptionDispatchInfo.Capture(stopFailure).Throw();
    }
}
