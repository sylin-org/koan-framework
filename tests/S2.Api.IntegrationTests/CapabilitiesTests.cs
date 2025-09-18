using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Json;
using Koan.Web.Extensions.Policies;
using Xunit;

namespace S2.Api.IntegrationTests;

public sealed class CapabilitiesTests
{
    [Fact]
    public async Task Moderation_SoftDelete_Audit_basic_flow_should_work()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("urls", "http://localhost:0");
            builder.UseSetting("DOTNET_ENVIRONMENT", "Development");
            builder.ConfigureServices(services =>
            {
                // Register permissive policies for tests (map all to role "Dev"), and add a fallback that always succeeds
                services.AddAuthorization(o =>
                {
                    o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
                });
                services.AddKoanWebCapabilityPolicies(opts =>
                {
                    opts.ModerationAuthorRole = "Dev";
                    opts.ModerationReviewerRole = "Dev";
                    opts.ModerationPublisherRole = "Dev";
                    opts.SoftDeleteRole = "Dev";
                    opts.AuditRole = "Dev";
                });
            });
        });

        var client = app.CreateClient();

        // Create an item
        var create = await client.PostAsJsonAsync("/api/items", new { name = "CapTest" });
        create.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent);
        var created = JToken.Parse(await create.Content.ReadAsStringAsync());
        var id = created["id"]?.ToString() ?? created["Id"]?.ToString() ?? created["value"]?.ToString();
        id.Should().NotBeNull();

        // Draft create and submit
        var draftCreate = await client.PostAsJsonAsync($"/api/items/{id}/draft", new { snapshot = new { id, name = "DraftName" } });
        draftCreate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var submit = await client.PostAsync($"/api/items/{id}/draft/submit", content: null);
        submit.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Reviewer queue and approve with transform
        var queue = await client.GetAsync("/api/items/moderation/queue?page=1&size=10");
        queue.StatusCode.Should().Be(HttpStatusCode.OK);
        var approve = await client.PostAsJsonAsync($"/api/items/{id}/moderate/approve", new { transform = new { name = "Published" } });
        approve.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify published entity reflects transform
        var get = await client.GetAsync($"/api/items/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JToken.Parse(await get.Content.ReadAsStringAsync());
        body["name"]!.Value<string>().Should().Be("Published");

        // Audit snapshot and list
        var snap = await client.PostAsync($"/api/items/{id}/audit/snapshot", content: null);
        snap.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var list = await client.GetAsync($"/api/items/{id}/audit");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        // Soft-delete and restore
        var sdel = await client.DeleteAsync($"/api/items/{id}/soft");
        sdel.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var deleted = await client.GetAsync("/api/items/deleted?page=1&size=10");
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
        var restore = await client.PostAsync($"/api/items/{id}/restore", content: null);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Revert to version 1 (if exists) - tolerate 404 if snapshot not present
        var revert = await client.PostAsJsonAsync($"/api/items/{id}/audit/revert", new { version = 1 });
        revert.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }
}
