using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Identity;
using Koan.Identity.Roles;
using Koan.Identity.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Identity.Web.Tests;

public sealed class ScopedRoleWebSpec
{
    private const string CookieScheme = "ScopedRoleCookie";
    private const string SelectorScheme = "ScopedRoleSelector";
    private static readonly JsonSerializerOptions Json = CreateJson();

    [Fact]
    public async Task Web_complement_is_discovered_and_keeps_scope_version_and_auth_boundaries()
    {
        typeof(RoleEngine).Assembly.GetTypes().Should().NotContain(type => typeof(ControllerBase).IsAssignableFrom(type),
            "the headless Identity package must not mount management routes");
        var database = Path.Combine(Path.GetTempPath(), $"koan-scoped-role-web-{Guid.CreateVersion7():n}.db");
        try
        {
            using var host = await Start(database);
            using var client = host.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");
            using (AppHost.PushScope(host.Services))
            using (var scope = host.Services.CreateScope())
            {
                var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
                var root = new ScopedRoleScopeRef("tenant:web", "space", "space:web");
                await engine.Register(new(root));
                await engine.Register(new(new(root.TenantId, "topic", "topic:web"), root));
                await engine.Register(new(new(root.TenantId, "topic", "topic:other"), root));
            }

            (await client.GetAsync("/api/identity/scoped-roles/descriptor")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            Authorize(client, bearer: true);
            (await client.GetAsync("/api/identity/scoped-roles/descriptor")).StatusCode.Should().Be(HttpStatusCode.OK);
            const string scopePath = "/api/identity/scoped-roles/tenant:web/topic/topic:web";
            var create = await client.PostAsJsonAsync($"{scopePath}/roles", new
            {
                name = "Reader",
                grants = new[] { new { capability = "discussion.read" } },
                id = "attacker-selected-global-id",
                tenantId = "tenant:forged",
                updatedBy = "attacker",
                version = 999,
            });
            create.StatusCode.Should().Be(HttpStatusCode.Created);
            var role = await create.Content.ReadFromJsonAsync<ScopedRoleManagementController.RoleResponse>(Json);
            role.Should().NotBeNull();
            role!.Version.Should().Be(1);
            role.Id.Should().NotBe("attacker-selected-global-id");
            create.Headers.ETag!.Tag.Should().Be("\"1\"");

            var oversizedName = await client.PostAsJsonAsync($"{scopePath}/roles", new
            {
                name = new string('n', ScopedRoleInputLimits.NameLength + 1),
                grants = Array.Empty<object>(),
            });
            oversizedName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var oversizedPresentation = await client.PostAsJsonAsync($"{scopePath}/roles", new
            {
                name = "Too much presentation",
                grants = Array.Empty<object>(),
                presentation = Enumerable.Range(0, ScopedRoleInputLimits.PresentationEntries + 1)
                    .ToDictionary(index => $"key:{index}", _ => "value"),
            });
            oversizedPresentation.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var nestedPreview = await client.PostAsJsonAsync($"{scopePath}/preview", new
            {
                subject = "participant:web",
                capability = "discussion.read",
                parameters = new { nested = new { value = "not-scalar" } },
            });
            nestedPreview.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var list = await client.GetFromJsonAsync<ScopedRoleManagementController.Page<ScopedRoleManagementController.RoleResponse>>(
                $"{scopePath}/roles?page=1&pageSize=10", Json);
            list!.TotalCount.Should().Be(1);
            list.Items.Should().ContainSingle(item => item.Id == role.Id);

            var missingVersion = await client.PutAsJsonAsync($"{scopePath}/roles/{role.Id}", new { name = "No version" });
            missingVersion.StatusCode.Should().Be((HttpStatusCode)428);

            using var stale = new HttpRequestMessage(HttpMethod.Put, $"{scopePath}/roles/{role.Id}")
            {
                Content = JsonContent.Create(new { name = "Stale" }),
            };
            stale.Headers.TryAddWithoutValidation("If-Match", "\"99\"");
            var staleResponse = await client.SendAsync(stale);
            staleResponse.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

            using var foreign = new HttpRequestMessage(HttpMethod.Put,
                $"/api/identity/scoped-roles/tenant:web/topic/topic:other/roles/{role.Id}")
            {
                Content = JsonContent.Create(new { name = "Relocated" }),
            };
            foreign.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            (await client.SendAsync(foreign)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var current = new HttpRequestMessage(HttpMethod.Put, $"{scopePath}/roles/{role.Id}")
            {
                Content = JsonContent.Create(new { name = "Reader renamed", updatedBy = "attacker" }),
            };
            current.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            var updated = await client.SendAsync(current);
            updated.StatusCode.Should().Be(HttpStatusCode.OK);
            updated.Headers.ETag!.Tag.Should().Be("\"2\"");

            Authorize(client, bearer: true, subject: "intruder:web");
            var existingDenied = await client.GetAsync($"{scopePath}/roles/{role.Id}");
            var absentDenied = await client.GetAsync(
                $"/api/identity/scoped-roles/tenant:web/topic/topic:absent/roles/{role.Id}");
            existingDenied.StatusCode.Should().Be(HttpStatusCode.NotFound);
            absentDenied.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await existingDenied.Content.ReadAsStringAsync()).Should().Be(await absentDenied.Content.ReadAsStringAsync(),
                "authorization and existence failures must have one externally indistinguishable shape");

            var existingDecision = await client.GetAsync($"{scopePath}/effective/discussion.read");
            var absentDecision = await client.GetAsync(
                "/api/identity/scoped-roles/tenant:web/topic/topic:absent/effective/discussion.read");
            existingDecision.StatusCode.Should().Be(HttpStatusCode.OK);
            absentDecision.StatusCode.Should().Be(HttpStatusCode.OK);
            (await existingDecision.Content.ReadAsStringAsync()).Should().Be(await absentDecision.Content.ReadAsStringAsync(),
                "a denied effective-access check must not disclose whether the scope exists");

            Authorize(client, bearer: true);
            var preview = await client.PostAsJsonAsync($"{scopePath}/preview", new
            {
                subject = "participant:web",
                capability = "discussion.read",
            });
            preview.StatusCode.Should().Be(HttpStatusCode.OK);
            var previewBody = await preview.Content.ReadAsStringAsync();
            previewBody.Should().NotContain("roleIds").And.NotContain("versions").And.NotContain("winningPolicyId")
                .And.NotContain("sourceId", "the HTTP preview is a privacy-safe decision, not a provenance dump");

            client.DefaultRequestHeaders.Authorization = null;
            client.DefaultRequestHeaders.Remove("X-Test-User");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", CreateCookie(host.Services, "owner:web"));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Test", "forged-header-does-not-bypass-csrf");
            var cookieMutation = await client.PostAsJsonAsync($"{scopePath}/roles", new
            {
                name = "Cookie forged",
                grants = Array.Empty<object>(),
            });
            cookieMutation.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            client.DefaultRequestHeaders.Remove("Cookie");
            Authorize(client, bearer: true);
            (await client.GetAsync("/api/identity/admin/identities")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "scoped authority must not confer the reserved global operator role");
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
        }
    }

    private static async Task<IHost> Start(string database)
    {
        var builder = Host.CreateDefaultBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.UseEnvironment("Test");
            web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={database};Pooling=False",
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
            }));
            web.ConfigureServices(services =>
            {
                services.AddHttpContextAccessor();
                services.AddSingleton<IIdentityActorAccessor, WebActorAccessor>();
                services.AddSingleton<IScopedRoleCatalogContributor, WebCatalog>();
                services.AddScoped<IScopedRoleAuthorityContributor, WebAuthority>();
                services.AddKoan();
                services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = SelectorScheme;
                        options.DefaultChallengeScheme = SelectorScheme;
                    })
                    .AddPolicyScheme(SelectorScheme, SelectorScheme, options =>
                        options.ForwardDefaultSelector = context =>
                            context.Request.Headers.Cookie.Any(value =>
                                value?.Contains($"{CookieScheme}=", StringComparison.Ordinal) == true)
                                ? CookieScheme
                                : WebAuthHandler.SchemeName)
                    .AddCookie(CookieScheme, options => options.Cookie.Name = CookieScheme)
                    .AddScheme<AuthenticationSchemeOptions, WebAuthHandler>(WebAuthHandler.SchemeName, _ => { });
                services.AddAuthorization();
            });
            web.Configure(_ => { });
        });
        return await builder.StartAsync(TestContext.Current.CancellationToken);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string CreateCookie(IServiceProvider services, string subject)
    {
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieScheme);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], WebAuthHandler.SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), CookieScheme);
        return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
    }

    private static void Authorize(HttpClient client, bool bearer, string subject = "owner:web")
    {
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-User", subject);
        client.DefaultRequestHeaders.Authorization = bearer
            ? new System.Net.Http.Headers.AuthenticationHeaderValue("Test", "credential") : null;
    }

    private sealed class WebCatalog : IScopedRoleCatalogContributor
    {
        public void Describe(ScopedRoleCatalogBuilder catalog)
        {
            catalog.Scope("space");
            catalog.Scope("topic", "space");
            catalog.Capability("discussion.read", ["space", "topic"], allowsAnonymous: true);
        }
    }

    private sealed class WebAuthority : IScopedRoleAuthorityContributor
    {
        private static readonly IReadOnlySet<ScopedRoleAuthorityOperation> Operations =
            Enum.GetValues<ScopedRoleAuthorityOperation>().ToHashSet();
        public ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
            ScopedRoleAuthorityRequest request, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>(
                request.Actor.Subject == "owner:web" || request.Actor.Subject == "owner:seed"
                    ? [new(request.Target, Operations, Descendants: true, ProofKey: "web-owner", ProofVersion: 1)]
                    : []);
        public ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
            ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
            => ValueTask.FromResult(envelope.ProofKey == "web-owner" && envelope.ProofVersion == 1);
    }

    private sealed class WebActorAccessor(IHttpContextAccessor http) : IIdentityActorAccessor
    {
        public string? CurrentActorSubject => http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "owner:seed";
    }

    private sealed class WebAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ScopedRoleTest";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.Authorization.Any(value =>
                    value?.StartsWith("Test ", StringComparison.OrdinalIgnoreCase) == true) ||
                !Request.Headers.TryGetValue("X-Test-User", out var subject))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
