using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity;
using Koan.Tenancy;
using Koan.Web.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SnapVault.Controllers;
using SnapVault.Models;
using SnapVault.Services;
using Xunit;
using PersonIdentity = Koan.Identity.Identity;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>
/// The share-link security surface through the real Web pipeline: Development identity contributes the person,
/// SnapVault validates the event selector against a durable grant, then tenant and PhotoAsset filters wrap all
/// downstream Entity work. Invalid/revoked selectors reject before controllers, and management remains operator-only.
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultWebContextSecuritySpec
{
    private readonly SnapVaultHostFixture _fixture;

    public SnapVaultWebContextSecuritySpec(SnapVaultHostFixture fixture) => _fixture = fixture;

    private T Service<T>() where T : notnull => _fixture.Host.Services.GetRequiredService<T>();
    private static string Stamp() => Guid.NewGuid().ToString("n")[..8];

    [Fact]
    public async Task Validated_link_scopes_reads_and_proofing_then_revocation_rejects_the_next_request()
    {
        var stamp = Stamp();
        var studio = "studio-" + stamp;
        var guestId = "guest-" + stamp;
        await new PersonIdentity { Id = guestId, DisplayName = "Client" }.Save();

        Event eventA;
        Event eventB;
        PhotoAsset photoA;
        PhotoAsset photoB;
        using (Tenant.Use(studio))
        {
            eventA = await new Event { Name = "A" }.Save();
            eventB = await new Event { Name = "B" }.Save();
            photoA = await new PhotoAsset { EventId = eventA.Id, OriginalFileName = "a.jpg" }.Save();
            photoB = await new PhotoAsset { EventId = eventB.Id, OriginalFileName = "b.jpg" }.Save();
        }

        var granted = await Service<GalleryGrantService>().GrantAsync(studio, eventA.Id, guestId);
        granted.Outcome.Should().Be(GalleryGrantOutcome.Granted);

        using var client = _fixture.CreateClient();
        var link = $"?_as={guestId}&event={eventA.Id}";
        var visible = await client.PostAsJsonAsync(
            "/api/photosets/query" + link,
            new PhotoSetQueryRequest
            {
                StartIndex = 0,
                Count = 20,
                Definition = new PhotoSetDefinition { Context = "event", EventId = eventA.Id },
            },
            TestContext.Current.CancellationToken);
        var visibleBody = await visible.Content.ReadFromJsonAsync<PhotoSetQueryResponse>(TestContext.Current.CancellationToken);

        visible.StatusCode.Should().Be(HttpStatusCode.OK);
        visibleBody!.Photos.Select(static photo => photo.Id).Should().Equal(photoA.Id);

        var crossEvent = await client.PostAsJsonAsync(
            "/api/photosets/query" + link,
            new PhotoSetQueryRequest
            {
                StartIndex = 0,
                Count = 20,
                Definition = new PhotoSetDefinition { Context = "event", EventId = eventB.Id },
            },
            TestContext.Current.CancellationToken);
        var crossBody = await crossEvent.Content.ReadFromJsonAsync<PhotoSetQueryResponse>(TestContext.Current.CancellationToken);
        crossBody!.Photos.Should().BeEmpty("the body cannot widen the validated link scope");

        var proof = await client.PostAsJsonAsync(
            $"/api/proofing/{photoA.Id}" + link,
            new ProofMarkRequest { Selected = true, Rating = 99 },
            TestContext.Current.CancellationToken);
        proof.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ProofSelection.Get(ProofSelection.KeyFor(guestId, photoA.Id)))!.Rating.Should().Be(5);

        var crossProof = await client.PostAsJsonAsync(
            $"/api/proofing/{photoB.Id}" + link,
            new ProofMarkRequest { Selected = true },
            TestContext.Current.CancellationToken);
        crossProof.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var management = await client.GetAsync("/api/photos/stats" + link, TestContext.Current.CancellationToken);
        management.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await granted.Grant!.Remove(TestContext.Current.CancellationToken);
        var revoked = await client.PostAsJsonAsync(
            "/api/photosets/query" + link,
            new PhotoSetQueryRequest
            {
                StartIndex = 0,
                Count = 20,
                Definition = new PhotoSetDefinition { Context = "event", EventId = eventA.Id },
            },
            TestContext.Current.CancellationToken);
        revoked.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Link_selects_one_granted_studio_instead_of_aggregating_a_subject_snapshot()
    {
        var stamp = Stamp();
        var guestId = "multi-" + stamp;
        await new PersonIdentity { Id = guestId, DisplayName = "Multi" }.Save();

        var studioA = "studio-a-" + stamp;
        var studioB = "studio-b-" + stamp;
        Event eventA;
        Event eventB;
        using (Tenant.Use(studioA)) eventA = await new Event { Name = "A" }.Save();
        using (Tenant.Use(studioB)) eventB = await new Event { Name = "B" }.Save();
        await Service<GalleryGrantService>().GrantAsync(studioA, eventA.Id, guestId);
        await Service<GalleryGrantService>().GrantAsync(studioB, eventB.Id, guestId);

        async Task<string?> ResolveTenant(string eventId)
        {
            await using var requestScope = _fixture.Host.Services.CreateAsyncScope();
            var services = requestScope.ServiceProvider;
            var http = new DefaultHttpContext { RequestServices = services };
            http.Request.QueryString = new QueryString($"?_as={guestId}&event={eventId}");
            var context = new WebContext(http);
            var entered = new List<IDisposable>();
            try
            {
                foreach (var contributor in services.GetServices<IWebContextContributor>().OrderBy(static item => item.Order))
                {
                    await contributor.ContributeAsync(context);
                    context.IsRejected.Should().BeFalse();
                    if (context.EnterPending() is { } scope) entered.Add(scope);
                }
                return Tenant.Current?.Id;
            }
            finally
            {
                for (var index = entered.Count - 1; index >= 0; index--) entered[index].Dispose();
            }
        }

        (await ResolveTenant(eventA.Id)).Should().Be(studioA);
        (await ResolveTenant(eventB.Id)).Should().Be(studioB);
    }

    [Fact]
    public void SnapVault_registers_one_application_context_contributor()
    {
        using var scope = _fixture.Host.Services.CreateScope();
        scope.ServiceProvider.GetServices<IWebContextContributor>()
            .Count(contributor => contributor.GetType().Assembly == typeof(global::SnapVault.Initialization.SnapVaultModule).Assembly)
            .Should().Be(1);
    }
}
