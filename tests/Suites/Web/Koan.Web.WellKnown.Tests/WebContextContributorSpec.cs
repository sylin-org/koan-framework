using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Context;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Context;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

public sealed class WebContextContributorSpec
{
    [Fact]
    public async Task Ordered_context_is_entered_between_contributors_and_scopes_raw_entity_reads()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Test");
                web.ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddKoanControllersFrom<ContextProbeController>();
                    services.AddScoped<IWebContextContributor, PrincipalContextContributor>();
                    services.AddScoped<IWebContextContributor, GalleryContextContributor>();
                });
                web.Configure(_ => { });
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await ContextWidget.RemoveAll(ct: TestContext.Current.CancellationToken);
            await new ContextWidget { Id = "visible", Group = "gallery" }.Save(TestContext.Current.CancellationToken);
            await new ContextWidget { Id = "hidden", Group = "other" }.Save(TestContext.Current.CancellationToken);

            var client = host.GetTestClient();
            var response = await client.GetAsync("/context-probe?gallery=gallery", TestContext.Current.CancellationToken);
            var body = await response.Content.ReadFromJsonAsync<ContextProbeResponse>(TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Should().NotBeNull();
            body!.SubjectId.Should().Be("alice");
            body.Marker.Should().Be("identity-ready");
            body.Ids.Should().Equal("visible");
            KoanContext.Get<ContextMarker>().Should().BeNull("request context must unwind after the endpoint completes");

            var rejected = await client.GetAsync("/context-probe?gallery=forbidden", TestContext.Current.CancellationToken);
            rejected.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}

public sealed class PrincipalContextContributor : IWebContextContributor
{
    public int Order => 0;

    public ValueTask ContributeAsync(WebContext context)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")], "test"));
        context.UsePrincipal(principal);
        context.Use(() => KoanContext.Push(new ContextMarker("identity-ready")));
        return ValueTask.CompletedTask;
    }
}

public sealed class GalleryContextContributor : IWebContextContributor
{
    public int Order => 100;

    public ValueTask ContributeAsync(WebContext context)
    {
        if (context.SubjectId != "alice" || KoanContext.Get<ContextMarker>()?.Value != "identity-ready")
            throw new InvalidOperationException("Earlier Web context was not entered before the next contributor.");

        var gallery = context.HttpContext.Request.Query["gallery"].ToString();
        if (gallery == "forbidden") context.Reject();
        else if (!string.IsNullOrEmpty(gallery)) context.Where<ContextWidget>(widget => widget.Group == gallery);
        return ValueTask.CompletedTask;
    }
}

public sealed record ContextMarker(string Value);

public sealed class ContextWidget : Entity<ContextWidget>
{
    public string Group { get; set; } = "";
}

public sealed record ContextProbeResponse(string? SubjectId, string? Marker, IReadOnlyList<string> Ids);

[ApiController]
[Route("context-probe")]
public sealed class ContextProbeController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ContextProbeResponse>> Get(CancellationToken ct)
    {
        var widgets = await ContextWidget.All(ct);
        return new ContextProbeResponse(
            User.FindFirst("sub")?.Value,
            KoanContext.Get<ContextMarker>()?.Value,
            widgets.Select(static widget => widget.Id).Order(StringComparer.Ordinal).ToArray());
    }
}
