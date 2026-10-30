using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// What a provider asserted, through the real challenge and callback: its roles become claims only where the provider
/// opts in, Koan's reserved roles never do, and every provider's sign-in stamps the subject qualified by the provider.
/// </summary>
public sealed class ProviderClaimsSpec
{
    private const string ReservedAndOrdinary = "editor,koan:identity-operator,KOAN:tenancy-operator";

    [Fact]
    public async Task A_provider_that_opts_in_maps_its_roles_but_never_a_reserved_one()
    {
        await using var app = await App.Start([]);

        var me = await app.SignInAndReadMe("/auth/test/challenge", ReservedAndOrdinary);

        me.Roles.Should().Contain("editor");
        me.Roles.Should().NotContain(role => role.StartsWith("koan:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_provider_that_has_not_opted_in_maps_no_role_at_all()
    {
        await using var app = await App.Start(new() { ["Koan:Web:Auth:Providers:test:MapRolesAndClaims"] = "false" });

        var me = await app.SignInAndReadMe("/auth/test/challenge", ReservedAndOrdinary);

        me.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task A_provider_sign_in_stamps_the_subject_qualified_by_the_provider()
    {
        await using var app = await App.Start([]);

        var me = await app.SignInAndReadMe("/auth/test/challenge", "editor");

        me.Claims["koan_provider_subject"].Should().Equal("test:alice@example.com");
    }

    private sealed record Me(string[] Roles, Dictionary<string, string[]> Claims);

    private sealed class App(IHost host, string listen) : IAsyncDisposable
    {
        public static async Task<App> Start(Dictionary<string, string?> settings)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var listen = $"http://127.0.0.1:{port}";
            var configuration = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:0",
                ["Logging:LogLevel:Default"] = "Warning",
            };
            foreach (var (key, value) in settings) configuration[key] = value;

            var host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(b => b.AddInMemoryCollection(configuration))
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseUrls(listen);
                    web.UseEnvironment("Development");
                    web.ConfigureServices(services =>
                    {
                        services.AddDataProtection().UseEphemeralDataProtectionProvider();
                        services.AddKoan();
                        services.AddKoanWeb();
                    });
                    web.Configure(_ => { });
                })
                .Build();
            try { await host.StartAsync(TestContext.Current.CancellationToken); }
            catch { host.Dispose(); throw; }
            return new App(host, listen);
        }

        /// <summary>Follows challenge, authorize and callback with the simulator asked for these roles, then reads <c>/me</c>.</summary>
        public async Task<Me> SignInAndReadMe(string challenge, string roles)
        {
            var ct = TestContext.Current.CancellationToken;
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
            handler.CookieContainer.Add(new Uri(listen), new Cookie("_tp_user", Uri.EscapeDataString("alice|alice@example.com")) { Path = "/" });
            using var client = new HttpClient(handler) { BaseAddress = new Uri(listen) };

            var url = new Uri(new Uri(listen), challenge);
            var hops = new List<string>();
            for (var hop = 0; hop < 8 && !url.AbsolutePath.EndsWith("/callback", StringComparison.Ordinal); hop++)
            {
                if (url.AbsolutePath.EndsWith("/authorize", StringComparison.Ordinal))
                    url = new Uri(QueryHelpers.AddQueryString(url.ToString(), "roles", roles));
                using var response = await client.GetAsync(url, ct);
                hops.Add($"{(int)response.StatusCode} {url.PathAndQuery}");
                response.StatusCode.Should().Be(HttpStatusCode.Redirect, string.Join(" -> ", hops));
                var location = response.Headers.Location!;
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
            }
            using (var callback = await client.GetAsync(url, ct))
                callback.StatusCode.Should().Be(HttpStatusCode.Redirect, string.Join(" -> ", hops));

            using var me = await client.GetAsync("/me", ct);
            me.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await me.Content.ReadAsStringAsync(ct));
            return new Me(
                document.RootElement.GetProperty("roles").EnumerateArray().Select(static role => role.GetString()!).ToArray(),
                document.RootElement.GetProperty("claims").EnumerateObject().ToDictionary(
                    static claim => claim.Name,
                    static claim => claim.Value.EnumerateArray().Select(static value => value.GetString()!).ToArray()));
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
