using System.Net;
using System.Net.Http;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core.Model;
using Koan.Web.Extensions.Authorization;
using Koan.Web.Extensions.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>An Entity an application put an audit controller over, and mapped no policy for.</summary>
public sealed class AuditedWidget : Entity<AuditedWidget>
{
    public string Name { get; set; } = "";
}

[Route("api/audited-widgets")]
public sealed class AuditedWidgetAuditController : EntityAuditController<AuditedWidget>;

/// <summary>
/// WEB-0083: a capability action with no policy is denied, whether or not the application ever called
/// <c>AddCapabilityAuthorization</c>, and the refusal says what to map. The declaration of a capability controller opens
/// no authority by itself.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class CapabilityDenyByDefaultSpec
{
    private readonly RestEntityWebFactory _fx;

    public CapabilityDenyByDefaultSpec(RestEntityWebFactory fx) => _fx = fx;

    private static HttpRequestMessage SignedIn(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Scopes", "any:scope");
        return request;
    }

    [Fact]
    public async Task A_signed_in_caller_cannot_use_an_action_with_no_policy()
    {
        var list = await _fx.Client.SendAsync(SignedIn(HttpMethod.Get, "/api/audited-widgets/one/audit"));
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var snapshot = await _fx.Client.SendAsync(SignedIn(HttpMethod.Post, "/api/audited-widgets/one/audit/snapshot"));
        snapshot.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_refusal_names_the_action_the_entity_and_what_to_map()
    {
        var list = await _fx.Client.SendAsync(SignedIn(HttpMethod.Get, "/api/audited-widgets/one/audit"));

        var body = await list.Content.ReadAsStringAsync();
        body.Should().Contain("Audit.List").And.Contain("AuditedWidget").And.Contain("caps.Defaults.Audit.List");
        body.Should().NotContain("AuditedWidgetAuditController", "the entity under the controller is what a policy maps");
    }

    [Fact]
    public void Options_deny_until_an_application_says_otherwise()
    {
        new CapabilityAuthorizationOptions().DefaultBehavior.Should().Be(CapabilityDefaultBehavior.Deny);
    }

    [Fact]
    public void An_application_s_declaration_wins_whichever_side_of_AddKoan_it_stands()
    {
        var before = new ServiceCollection();
        before.AddCapabilityAuthorization(caps => caps.DefaultBehavior = CapabilityDefaultBehavior.Allow);
        before.AddKoan();

        var after = new ServiceCollection();
        after.AddKoan();
        after.AddCapabilityAuthorization(caps => caps.DefaultBehavior = CapabilityDefaultBehavior.Allow);

        Declared(before).DefaultBehavior.Should().Be(CapabilityDefaultBehavior.Allow);
        Declared(after).DefaultBehavior.Should().Be(CapabilityDefaultBehavior.Allow);
    }

    private static CapabilityAuthorizationOptions Declared(IServiceCollection services)
        => services.Where(descriptor => descriptor.ServiceType == typeof(CapabilityAuthorizationOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CapabilityAuthorizationOptions>()
            .Should().ContainSingle().Subject;
}
