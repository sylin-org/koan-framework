using System.Text.Json;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// WEB-0081: with nothing configured, the connector's client identity derives from the URL the application accepted,
/// and accepting another moves the client-metadata URL and the redirect on the next request, with no restart. With
/// nothing accepted either, Development derives it from the request, so an application boots before it accepts.
/// </summary>
public sealed class AcceptedClientIdentity
{
    [Fact]
    public async Task The_client_identity_follows_the_accepted_url_without_a_restart()
    {
        var store = new MemoryApplicationUrlStore("https://first.example");
        await using var host = await AtprotoHost.Start(store);

        var (clientId, redirect) = await host.Metadata();
        Assert.Equal("https://first.example/auth/atproto/client-metadata.json", clientId);
        Assert.Equal("https://first.example/auth/atproto/callback", redirect);

        await host.Services.GetRequiredService<KoanWebBaseUrl>().Accept("https://second.example", TestContext.Current.CancellationToken);

        (clientId, redirect) = await host.Metadata();
        Assert.Equal("https://second.example/auth/atproto/client-metadata.json", clientId);
        Assert.Equal("https://second.example/auth/atproto/callback", redirect);
        Assert.Equal("https://second.example", store.Value);
    }

    [Fact]
    public async Task In_production_a_stored_url_names_the_client_although_the_host_composes_before_reading_it()
    {
        await using var host = await AtprotoHost.Start(new MemoryApplicationUrlStore("https://place.example"), "Production");

        var (clientId, redirect) = await host.Metadata();

        Assert.Equal("https://place.example/auth/atproto/client-metadata.json", clientId);
        Assert.Equal("https://place.example/auth/atproto/callback", redirect);
        // Web Auth compiles its provider plan while the host composes, before the store is read: it must not refuse then.
        Assert.DoesNotContain(host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts,
            fact => fact.State == KoanFactState.CollectionFailed && fact.Subject.StartsWith("Sylin.Koan.Web.Auth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task In_production_a_configured_url_that_cannot_name_a_client_refuses_the_start_although_a_store_is_registered()
    {
        var start = () => AtprotoHost.Start(new MemoryApplicationUrlStore(null), "Production",
            new() { ["Koan:Web:ApplicationUrl"] = "http://www.example.com" });

        var failure = await Assert.ThrowsAnyAsync<Exception>(start);
        Assert.Contains(Messages(failure), message =>
            message.Contains("http://www.example.com") && message.Contains("Koan:Web:ApplicationUrl") && message.Contains("ClientId")
            && !message.Contains("accept it"));
    }

    [Fact]
    public async Task In_production_a_store_that_holds_nothing_without_the_posture_refuses_the_start_once_it_is_read()
    {
        var start = () => AtprotoHost.Start(new MemoryApplicationUrlStore(null), "Production");

        var failure = await Assert.ThrowsAnyAsync<Exception>(start);
        Assert.Contains(Messages(failure), message =>
            message.Contains("Koan:Web:ApplicationUrl") && message.Contains("accept it") && message.Contains("ClientId"));
    }

    [Fact]
    public async Task In_production_a_stored_development_url_without_the_posture_is_nothing_accepted_and_refuses_the_start_as_that()
    {
        var start = () => AtprotoHost.Start(new MemoryApplicationUrlStore("http://127.0.0.1:5220"), "Production");

        var failure = await Assert.ThrowsAnyAsync<Exception>(start);
        Assert.Contains(Messages(failure), message =>
            message.Contains("there is none") && message.Contains("accept it") && message.Contains("ClientId"));
        Assert.DoesNotContain(Messages(failure), message => message.Contains("Correct or clear"));
    }

    [Fact]
    public async Task In_production_a_stored_development_url_with_the_posture_starts_and_a_later_accept_names_the_client()
    {
        var store = new MemoryApplicationUrlStore("http://127.0.0.1:5220", derivesUntilAccepted: true);
        await using var host = await AtprotoHost.Start(store, "Production");
        var baseUrl = host.Services.GetRequiredService<KoanWebBaseUrl>();

        Assert.Null(baseUrl.Accepted);
        Assert.True(baseUrl.DerivesFromRequests);

        await baseUrl.Accept("https://place.example", TestContext.Current.CancellationToken);

        var (clientId, _) = await host.Metadata();
        Assert.Equal("https://place.example/auth/atproto/client-metadata.json", clientId);
        Assert.Equal("https://place.example", store.Value);
    }

    [Fact]
    public async Task In_production_the_posture_defers_the_identity_to_the_request()
    {
        await using var host = await AtprotoHost.Start(new MemoryApplicationUrlStore(null, derivesUntilAccepted: true), "Production");

        Assert.True(host.Services.GetRequiredService<KoanWebBaseUrl>().DerivesFromRequests);
    }

    [Fact]
    public async Task With_nothing_accepted_development_derives_the_identity_from_the_request()
    {
        await using var host = await AtprotoHost.Start(new MemoryApplicationUrlStore(null));

        var (clientId, redirect) = await host.Metadata();

        Assert.Equal("http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%2Fauth%2Fatproto%2Fcallback&scope=atproto", clientId);
        Assert.Equal("http://127.0.0.1/auth/atproto/callback", redirect);
    }

    /// <summary>An in-memory Koan host (TestServer) in Development with the atproto provider and no configured base URL.</summary>
    private sealed class AtprotoHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly string _sessions;

        private AtprotoHost(IHost host, string sessions) { _host = host; _sessions = sessions; }

        public IServiceProvider Services => _host.Services;

        public async Task<(string ClientId, string Redirect)> Metadata()
        {
            using var client = _host.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");
            using var response = await client.GetAsync("/auth/atproto/client-metadata.json", TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return (document.RootElement.GetProperty("client_id").GetString()!,
                document.RootElement.GetProperty("redirect_uris").EnumerateArray().Single().GetString()!);
        }

        public static async Task<AtprotoHost> Start(
            IApplicationUrlStore store, string environment = "Development", Dictionary<string, string?>? extra = null)
        {
            var sessions = Path.Combine(Path.GetTempPath(), "koan-atproto-" + Guid.NewGuid().ToString("N"));
            var settings = new Dictionary<string, string?>
            {
                ["Koan:Environment"] = environment,
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Koan:Web:Auth:Providers:atproto:Scopes:0"] = "atproto",
                ["Koan:Web:Auth:Atproto:SessionDirectory"] = sessions,
            };
            if (extra is not null) foreach (var (key, value) in extra) settings[key] = value;

            var host = Host.CreateDefaultBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.UseEnvironment(environment);
                    web.UseContentRoot(AppContext.BaseDirectory);
                    web.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                    web.ConfigureServices(services =>
                    {
                        services.AddSingleton(store);
                        services.AddKoan();
                    });
                    web.Configure(_ => { });
                })
                .Build();
            try { await host.StartAsync(TestContext.Current.CancellationToken); }
            catch
            {
                // Release the AppHost lease a start that failed in module start leaves behind.
                try { await host.StopAsync(CancellationToken.None); } catch { /* the start already failed */ }
                host.Dispose();
                throw;
            }
            return new AtprotoHost(host, sessions);
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
            try { Directory.Delete(_sessions, recursive: true); } catch (IOException) { }
        }
    }

    private static IEnumerable<string> Messages(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions)
                    foreach (var message in Messages(inner)) yield return message;
        }
    }

    private sealed class MemoryApplicationUrlStore(string? value, bool derivesUntilAccepted = false) : IApplicationUrlStore
    {
        public string? Value { get; private set; } = value;

        public bool DerivesUntilAccepted => derivesUntilAccepted;

        public Task<string?> Load(CancellationToken ct) => Task.FromResult(Value);

        public Task Save(string applicationUrl, CancellationToken ct)
        {
            Value = applicationUrl;
            return Task.CompletedTask;
        }
    }
}
