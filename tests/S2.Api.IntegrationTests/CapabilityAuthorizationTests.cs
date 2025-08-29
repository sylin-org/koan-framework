using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Sora.Web.Extensions.Authorization;
using Xunit;

namespace S2.Api.IntegrationTests;

public sealed class CapabilityAuthorizationTests : IClassFixture<MongoFixture>
{
    private readonly MongoFixture _fx;
    public CapabilityAuthorizationTests(MongoFixture fx) => _fx = fx;

    [Fact]
    public async Task deny_by_default_with_no_mappings_should_return_403()
    {
        if (!_fx.Available) return; // skip when Mongo isn't available
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("urls", "http://localhost:0");
                builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _fx.ConnectionString,
                        ["Sora:Data:Mongo:Database"] = "s2_cap_auth"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    // Let [Authorize] succeed for anonymous test client
                    services.AddAuthorization(o =>
                    {
                        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAssertion(_ => true)
                            .Build();
                    });

                    services.AddCapabilityAuthorization(opts =>
                    {
                        opts.DefaultBehavior = CapabilityDefaultBehavior.Deny;
                        // No Defaults or Entity mappings
                    });
                });
            });

        var client = app.CreateClient();
    var resp = await client.GetAsync("/api/items/moderation/queue?page=1&size=10");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    // ProblemDetails shape
    var problem = await resp.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
    problem.Should().NotBeNull();
    problem!.Status.Should().Be((int)HttpStatusCode.Forbidden);
    problem.Title.Should().Be("Forbidden");
    problem.Detail.Should().Contain("Capability");
    }

    [Fact]
    public async Task deny_by_default_softdelete_list_should_return_403()
    {
        if (!_fx.Available) return;
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("urls", "http://localhost:0");
                builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _fx.ConnectionString,
                        ["Sora:Data:Mongo:Database"] = "s2_cap_auth"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddAuthorization(o =>
                    {
                        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAssertion(_ => true)
                            .Build();
                    });

                    services.AddCapabilityAuthorization(opts =>
                    {
                        opts.DefaultBehavior = CapabilityDefaultBehavior.Deny;
                    });
                });
            });

        var client = app.CreateClient();
        var resp = await client.GetAsync("/api/items/soft-delete/deleted?page=1&size=10");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task deny_by_default_audit_list_should_return_403()
    {
        if (!_fx.Available) return;
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("urls", "http://localhost:0");
                builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _fx.ConnectionString,
                        ["Sora:Data:Mongo:Database"] = "s2_cap_auth"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddAuthorization(o =>
                    {
                        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAssertion(_ => true)
                            .Build();
                    });

                    services.AddCapabilityAuthorization(opts =>
                    {
                        opts.DefaultBehavior = CapabilityDefaultBehavior.Deny;
                    });
                });
            });

        var client = app.CreateClient();
        // Any id is fine; auth happens before repository lookup
        var resp = await client.GetAsync("/api/items/any-id/audit");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task deny_by_default_moderation_approve_reject_return_should_return_403()
    {
        if (!_fx.Available) return;
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("urls", "http://localhost:0");
                builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _fx.ConnectionString,
                        ["Sora:Data:Mongo:Database"] = "s2_cap_auth"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddAuthorization(o =>
                    {
                        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAssertion(_ => true)
                            .Build();
                    });
                    services.AddCapabilityAuthorization(opts => opts.DefaultBehavior = CapabilityDefaultBehavior.Deny);
                });
            });

        var client = app.CreateClient();
        var id = "abc";
        var approve = await client.PostAsJsonAsync($"/api/items/{id}/moderation/approve", new { });
        var reject  = await client.PostAsJsonAsync($"/api/items/{id}/moderation/reject", new { reason = "nope" });
        var ret     = await client.PostAsJsonAsync($"/api/items/{id}/moderation/return", new { reason = "fix" });
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reject.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ret.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task deny_by_default_softdelete_restore_and_audit_revert_should_return_403()
    {
        if (!_fx.Available) return;
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("urls", "http://localhost:0");
                builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _fx.ConnectionString,
                        ["Sora:Data:Mongo:Database"] = "s2_cap_auth"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddAuthorization(o =>
                    {
                        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAssertion(_ => true)
                            .Build();
                    });
                    services.AddCapabilityAuthorization(opts => opts.DefaultBehavior = CapabilityDefaultBehavior.Deny);
                });
            });

        var client = app.CreateClient();
        var id = "abc";
        var restore = await client.PostAsJsonAsync($"/api/items/{id}/soft-delete/restore", new { targetSet = (string?)null });
        var revert  = await client.PostAsJsonAsync($"/api/items/{id}/audit/revert", new { version = 1 });
        restore.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revert.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task defaults_mapping_allows_action_when_strict_global()
    {
        if (!_fx.Available) return;
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("urls", "http://localhost:0");
                builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _fx.ConnectionString,
                        ["Sora:Data:Mongo:Database"] = "s2_cap_auth"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddAuthorization(o =>
                    {
                        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAssertion(_ => true)
                            .Build();
                        o.AddPolicy("allow-all", p => p.RequireAssertion(_ => true));
                    });

                    services.AddCapabilityAuthorization(opts =>
                    {
                        opts.DefaultBehavior = CapabilityDefaultBehavior.Deny;
                        opts.Defaults = new CapabilityPolicy
                        {
                            Moderation = new ModerationPolicy
                            {
                                Queue = "allow-all"
                            }
                        };
                    });
                });
            });

        var client = app.CreateClient();
    var resp = await client.GetAsync("/api/items/moderation/queue?page=1&size=10");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task entity_override_should_take_precedence_over_defaults()
    {
        if (!_fx.Available) return;
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("urls", "http://localhost:0");
                builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _fx.ConnectionString,
                        ["Sora:Data:Mongo:Database"] = "s2_cap_auth"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddAuthorization(o =>
                    {
                        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAssertion(_ => true)
                            .Build();
                        o.AddPolicy("allow-all", p => p.RequireAssertion(_ => true));
                        o.AddPolicy("deny-all", p => p.RequireAssertion(_ => false));
                    });

                    services.AddCapabilityAuthorization(opts =>
                    {
                        opts.DefaultBehavior = CapabilityDefaultBehavior.Deny;
                        opts.Defaults = new CapabilityPolicy
                        {
                            Moderation = new ModerationPolicy { Queue = "allow-all" }
                        };
                        // S2.Api exposes Item as the entity name for generic controllers
                        opts.Entities["Item"] = new CapabilityPolicy
                        {
                            Moderation = new ModerationPolicy { Queue = "deny-all" }
                        };
                    });
                });
            });

        var client = app.CreateClient();
    var resp = await client.GetAsync("/api/items/moderation/queue?page=1&size=10");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task allow_by_default_should_allow_unmapped_actions()
    {
        if (!_fx.Available) return;
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("urls", "http://localhost:0");
                builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _fx.ConnectionString,
                        ["Sora:Data:Mongo:Database"] = "s2_cap_auth"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddAuthorization(o =>
                    {
                        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAssertion(_ => true)
                            .Build();
                    });

                    services.AddCapabilityAuthorization(opts =>
                    {
                        opts.DefaultBehavior = CapabilityDefaultBehavior.Allow;
                        // No mappings at all
                    });
                });
            });

    var client = app.CreateClient();
    var resp = await client.GetAsync("/api/items/moderation/queue?page=1&size=10");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
