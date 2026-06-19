using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Koan.Web.Authorization;
using Koan.Web.Extensions.Authorization;
using Koan.Web.Extensions.Capabilities;
using Koan.Web.Hooks;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0002 step 3 — parity: the seam (RBAC floor + PolicyAuthorizationProvider) reproduces the characterized
/// WEB-0047 behavior bit-for-bit (mirrors <see cref="CapabilityAuthorizationCharacterizationTests"/>), and a
/// non-capability action correctly defers to the seam's general default instead of the capability DefaultBehavior.
/// </summary>
public sealed class PolicyAuthorizationProviderTests
{
    private sealed class Widget { }

    private sealed class FakeAuthz : IAuthorizationService
    {
        private readonly Func<string, bool> _ok;
        public FakeAuthz(Func<string, bool> ok) => _ok = ok;
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(_ok(policyName) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private static readonly ClaimsPrincipal User = new(new ClaimsIdentity("test"));
    private const string Action = CapabilityActions.SoftDelete.Delete;

    private static IAuthorize Seam(CapabilityAuthorizationOptions caps, Func<string, bool>? authz = null)
    {
        var providers = new IAuthorizationProvider[]
        {
            new RbacAuthorizationProvider(),
            new PolicyAuthorizationProvider(caps, new FakeAuthz(authz ?? (_ => false))),
        };
        return new Authorizer(providers, Microsoft.Extensions.Options.Options.Create(new AuthorizeOptions()));
    }

    private static async Task<bool> Allowed(IAuthorize seam, string action)
        => (await seam.AuthorizeAsync(new AuthorizeRequest { Subject = User, Action = action, Resource = typeof(Widget) })) is AuthorizeDecision.Allow;

    [Fact]
    public async Task Unmapped_action_allow_by_default()
        => (await Allowed(Seam(new CapabilityAuthorizationOptions { DefaultBehavior = CapabilityDefaultBehavior.Allow }), Action)).Should().BeTrue();

    [Fact]
    public async Task Unmapped_action_deny_by_default()
        => (await Allowed(Seam(new CapabilityAuthorizationOptions { DefaultBehavior = CapabilityDefaultBehavior.Deny }), Action)).Should().BeFalse();

    [Fact]
    public async Task Mapped_default_policy_is_evaluated()
    {
        var caps = new CapabilityAuthorizationOptions();
        caps.Defaults.SoftDelete.Delete = "can-delete";
        (await Allowed(Seam(caps, p => p == "can-delete"), Action)).Should().BeTrue();
        (await Allowed(Seam(caps, _ => false), Action)).Should().BeFalse();
    }

    [Fact]
    public async Task Entity_mapping_overrides_defaults()
    {
        var caps = new CapabilityAuthorizationOptions();
        caps.Defaults.SoftDelete.Delete = "default-policy";
        caps.Entities["Widget"] = new CapabilityPolicy();
        caps.Entities["Widget"].SoftDelete.Delete = "widget-policy";
        (await Allowed(Seam(caps, p => p == "widget-policy"), Action)).Should().BeTrue();
    }

    [Fact]
    public async Task Empty_entity_mapping_falls_back_to_defaults()
    {
        var caps = new CapabilityAuthorizationOptions();
        caps.Defaults.SoftDelete.Delete = "default-policy";
        caps.Entities["Widget"] = new CapabilityPolicy();
        (await Allowed(Seam(caps, p => p == "default-policy"), Action)).Should().BeTrue();
    }

    [Fact]
    public async Task Non_capability_action_uses_seam_default_not_capability_DefaultBehavior()
        // capability DefaultBehavior.Deny must NOT govern a non-capability action — the provider defers and
        // the seam's general default (Allow) applies.
        => (await Allowed(Seam(new CapabilityAuthorizationOptions { DefaultBehavior = CapabilityDefaultBehavior.Deny }), "not.a.capability.action"))
            .Should().BeTrue();
}
