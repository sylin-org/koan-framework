using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Koan.Web.Extensions.Authorization;
using Koan.Web.Extensions.Capabilities;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0002 step 0 — characterization of the current WEB-0047 capability authorization behavior
/// (Entity-mapping → Defaults → DefaultBehavior; mapped policies evaluated by IAuthorizationService).
/// Pins the contract BEFORE the logic moves into PolicyAuthorizationProvider, so the new path can be
/// proven bit-for-bit identical (copy-then-verify-then-delete).
/// </summary>
public sealed class CapabilityAuthorizationCharacterizationTests
{
    private sealed class Widget { } // entityType.Name == "Widget"

    private sealed class FakeAuthz : IAuthorizationService
    {
        private readonly Func<string, bool> _succeeds;
        public FakeAuthz(Func<string, bool> succeeds) => _succeeds = succeeds;

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(_succeeds(policyName) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private static readonly ClaimsPrincipal User = new(new ClaimsIdentity("test"));

    private static CapabilityAuthorizer Sut(CapabilityAuthorizationOptions opts, Func<string, bool>? authz = null)
        => new(opts, new FakeAuthz(authz ?? (_ => false)));

    private const string Action = CapabilityActions.SoftDelete.Delete;

    [Fact]
    public void Unmapped_action_is_allowed_when_DefaultBehavior_is_Allow()
        => Sut(new CapabilityAuthorizationOptions { DefaultBehavior = CapabilityDefaultBehavior.Allow })
            .IsAllowed(User, typeof(Widget), Action).Should().BeTrue();

    [Fact]
    public void Unmapped_action_is_denied_when_DefaultBehavior_is_Deny()
        => Sut(new CapabilityAuthorizationOptions { DefaultBehavior = CapabilityDefaultBehavior.Deny })
            .IsAllowed(User, typeof(Widget), Action).Should().BeFalse();

    [Fact]
    public void Mapped_default_policy_is_evaluated_by_the_authorization_service()
    {
        var opts = new CapabilityAuthorizationOptions();
        opts.Defaults.SoftDelete.Delete = "can-delete";

        Sut(opts, p => p == "can-delete").IsAllowed(User, typeof(Widget), Action).Should().BeTrue();
        Sut(opts, _ => false).IsAllowed(User, typeof(Widget), Action).Should().BeFalse();
    }

    [Fact]
    public void Entity_mapping_overrides_defaults()
    {
        var opts = new CapabilityAuthorizationOptions();
        opts.Defaults.SoftDelete.Delete = "default-policy";
        opts.Entities["Widget"] = new CapabilityPolicy();
        opts.Entities["Widget"].SoftDelete.Delete = "widget-policy";

        // Only the entity policy is consulted — the default would not be asked.
        Sut(opts, p => p == "widget-policy").IsAllowed(User, typeof(Widget), Action).Should().BeTrue();
    }

    [Fact]
    public void Empty_entity_mapping_falls_back_to_defaults()
    {
        var opts = new CapabilityAuthorizationOptions();
        opts.Defaults.SoftDelete.Delete = "default-policy";
        opts.Entities["Widget"] = new CapabilityPolicy(); // SoftDelete.Delete unset → fall back to Defaults

        Sut(opts, p => p == "default-policy").IsAllowed(User, typeof(Widget), Action).Should().BeTrue();
    }
}
