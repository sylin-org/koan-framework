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
            (await client.PutAsync($"/api/identity/roles/{key}/members/person:web-two", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            var first = await client.GetStringAsync($"/api/identity/roles/{key}/members?take=1");
            first.Should().Contain("person:web");
            var next = Next(first);
            next.Should().EndWith(":person:web", "the cursor names the last row returned");
            var second = await client.GetStringAsync($"/api/identity/roles/{key}/members?after={Uri.EscapeDataString(next!)}&take=1");
            second.Should().Contain("person:web-two").And.NotContain("\"next\"", "the last page has no next");
            (await client.GetAsync($"/api/identity/roles/{key}/members?take=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.DeleteAsync($"/api/identity/roles/{key}/members/person:web-two")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await client.DeleteAsync($"/api/identity/roles/{key}/members/person:web-two")).StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    [Fact]
    public async Task A_member_bound_answers_409_with_its_correction()
    {
        var database = Path.Combine(Path.GetTempPath(), $"koan-role-bound-web-{Guid.CreateVersion7():n}.db");
        try
        {
            using var host = await Start(database, new() { ["Koan:Identity:Roles:MaxRolesPerPerson"] = "1" });
            using var client = host.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");
            Authorize(client, "operator:web", true);
            foreach (var key in new[] { "role:small", "role:other" })
                (await client.PutAsJsonAsync($"/api/identity/roles/{key}", new { name = key, permissions = new[] { "global:post" } }))
                    .StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.PutAsync("/api/identity/roles/role:small/members/person:one", null)).StatusCode.Should().Be(HttpStatusCode.OK);

            var full = await client.PutAsync("/api/identity/roles/role:other/members/person:one", null);
            full.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await full.Content.ReadAsStringAsync()).Should().Contain("MaxRolesPerPerson").And.Contain("Remove one of their roles, or raise the bound");
        }
        finally { if (File.Exists(database)) File.Delete(database); }
    }

    [Fact]
    public async Task Web_complement_manages_overwrites_beside_the_role_routes()
    {
        var database = Path.Combine(Path.GetTempPath(), $"koan-overwrite-web-{Guid.CreateVersion7():n}.db");
        try
        {
            // A configuration still naming a removed bound starts and bounds nothing.
            using var host = await Start(database, new() { ["Koan:Identity:Roles:MaxOverwritesPerSubject"] = "1" });
            using var client = host.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");
            const string resource = "tangent/the workshop";
            var query = $"?resource={Uri.EscapeDataString(resource)}";
            (await client.GetAsync($"/api/identity/overwrites{query}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            Authorize(client, "person:visitor", false);
            (await client.GetAsync($"/api/identity/overwrites{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            Authorize(client, "operator:web", true);
            (await client.GetStringAsync("/api/identity/roles/descriptor")).Should().Contain(RoleTokens.Administrator).And.Contain("maxRolesPerPerson").And.NotContain("maxOverwritesPerResource");
            (await client.PutAsJsonAsync("/api/identity/roles/role:guests", new { name = "Guests", permissions = new[] { "global:post" } }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var saved = await client.PutAsJsonAsync($"/api/identity/overwrites/role/role:guests{query}", new { deny = new[] { "global:post" } });
            saved.StatusCode.Should().Be(HttpStatusCode.OK);
            (await saved.Content.ReadAsStringAsync()).Should().Contain(resource).And.Contain("global:post");
            (await client.PutAsJsonAsync($"/api/identity/overwrites/role/role:guests?resource=tangent%2Felsewhere", new { deny = new[] { "global:post" } }))
                .StatusCode.Should().Be(HttpStatusCode.OK, "a subject holds any number of overwrites");
            (await client.PutAsJsonAsync($"/api/identity/overwrites/person/person:web{query}", new { allow = new[] { "global:post" } }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.PutAsJsonAsync($"/api/identity/overwrites/role/everyone{query}", new { allow = new[] { "global:view" } }))
                .StatusCode.Should().Be(HttpStatusCode.OK, "a resource holds any number of overwrites");
            var listed = await client.GetStringAsync($"/api/identity/overwrites{query}");
            listed.Should().Contain("role:guests").And.Contain("person:web").And.Contain("everyone").And.NotContain("\"next\"", "the last page has no next");
            var paged = await client.GetStringAsync($"/api/identity/overwrites{query}&take=2");
            paged.Should().Contain("everyone").And.Contain("role:guests").And.NotContain("person:web");
            var after = Next(paged);
            after.Should().Be("Person:", "the resource's roles filled the page, so the next one starts with its people");
            (await client.GetStringAsync($"/api/identity/overwrites{query}&after={Uri.EscapeDataString(after!)}&take=2"))
                .Should().Contain("person:web").And.NotContain("\"next\"", "the last page has no next");
            (await client.GetAsync($"/api/identity/overwrites{query}&after=nonsense")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.PutAsJsonAsync($"/api/identity/overwrites/role/role:missing{query}", new { allow = new[] { "global:post" } }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PutAsJsonAsync($"/api/identity/overwrites/role/role:guests{query}", new { allow = new[] { RoleTokens.Administrator } }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            (await client.DeleteAsync($"/api/identity/overwrites/person/person:web{query}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await client.DeleteAsync($"/api/identity/overwrites/person/person:web{query}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetStringAsync($"/api/identity/overwrites{query}")).Should().NotContain("person:web");
        }
        finally { if (File.Exists(database)) File.Delete(database); }
    }

    private static async Task<IHost> Start(string database, Dictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateDefaultBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer(); web.UseEnvironment("Test");
            web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test", ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={database};Pooling=False",
                ["Koan:BackgroundServices:Enabled"] = "false", ["Logging:LogLevel:Default"] = "Warning",
            }.Concat(settings ?? [])));
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

    private static string? Next(string page)
        => System.Text.Json.JsonDocument.Parse(page).RootElement.TryGetProperty("next", out var next) ? next.GetString() : null;

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
