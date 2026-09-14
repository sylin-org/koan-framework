using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Koan.Core;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Identity.Web.Tests;

public sealed class RoleWebSpec
{
    [Fact]
    public async Task Web_complement_exposes_bounded_role_catalog_members_and_current_bag()
    {
        var database = Path.Combine(Path.GetTempPath(), $"koan-role-web-{Guid.CreateVersion7():n}.db");
        try
        {
            using var host = await Start(database);
            using var client = host.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");
            (await client.GetAsync("/api/identity/roles/descriptor")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            Authorize(client, "operator:web", true);
            (await client.GetAsync("/api/identity/roles/descriptor")).StatusCode.Should().Be(HttpStatusCode.OK);

            var key = $"role:member:{Guid.NewGuid():N}";
            var saved = await client.PutAsJsonAsync($"/api/identity/roles/{key}", new
            {
                name = "Member", permissions = new[] { "global:topic_read" },
                metadata = new Dictionary<string, string> { ["color"] = "#45a67f", ["purpose"] = "Community" },
            });
            saved.StatusCode.Should().Be(HttpStatusCode.OK);
            (await saved.Content.ReadAsStringAsync()).Should().Contain("#45a67f").And.Contain("global:topic_read");

            (await client.PutAsync($"/api/identity/roles/{key}/members/person:web", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var members = await client.GetStringAsync($"/api/identity/roles/{key}/members?page=1&pageSize=10");
            members.Should().Contain("person:web");
            (await client.GetStringAsync("/api/identity/roles?search=Member&page=1&pageSize=10")).Should().Contain(key);
            (await client.GetAsync("/api/identity/roles?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var tooMuchMetadata = Enumerable.Range(0, 33).ToDictionary(index => $"key:{index}", _ => "value");
            (await client.PutAsJsonAsync("/api/identity/roles/role:oversized", new
            {
                name = "Oversized", permissions = Array.Empty<string>(), metadata = tooMuchMetadata,
            })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            Authorize(client, "person:web", false);
            var bag = await client.GetStringAsync("/api/identity/roles/me/bag");
            bag.Should().Contain(key).And.Contain("global:topic_read").And.Contain("authenticated");
            (await client.GetAsync("/api/identity/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally { if (File.Exists(database)) File.Delete(database); }
    }

    private static async Task<IHost> Start(string database)
    {
        var builder = Host.CreateDefaultBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer(); web.UseEnvironment("Test");
            web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test", ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={database};Pooling=False",
                ["Koan:BackgroundServices:Enabled"] = "false", ["Logging:LogLevel:Default"] = "Warning",
            }));
            web.ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddAuthentication(TestAuth.SchemeName).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.SchemeName, _ => { });
                services.AddAuthorization();
            });
            web.Configure(_ => { });
        });
        return await builder.StartAsync(TestContext.Current.CancellationToken);
    }

    private static void Authorize(HttpClient client, string subject, bool op)
    {
        client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Remove("X-Test-Operator");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", subject);
        if (op) client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Operator", "true");
        client.DefaultRequestHeaders.Authorization = new("Test", "credential");
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RoleTest";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var subject)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject.ToString()) };
            if (Request.Headers.ContainsKey("X-Test-Operator")) claims.Add(new(ClaimTypes.Role, IdentityRoles.Operator));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
