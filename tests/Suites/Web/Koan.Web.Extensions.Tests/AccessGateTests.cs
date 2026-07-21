using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0004 (§A) — the per-action gate the entity-wide floor never had. Drives the reshaped
/// <see cref="EntityFloorAuthorizationProvider"/> (via the real <see cref="AccessGateCache"/>) over <c>[Access]</c>
/// entities and the parser/evaluator directly. The legacy-sugar non-regression matrix lives in
/// <see cref="EntityFloorAuthorizationProviderTests"/>; this pins the new behavior.
/// </summary>
public sealed class AccessGateTests
{
    // ── [Access] test entities (the provider reflects the attribute off the Type) ────────────────────────────────
    [Access(read: "anyone", write: "authenticated", remove: "is:admin")]
    private sealed class PublicReadEntity { }

    [Access(write: "authenticated")] // read + remove unspecified → OPEN (the per-action proof)
    private sealed class WriteOnlyGatedEntity { }

    [Access(write: "is:member,is:staff")] // OR of two single-term bags
    private sealed class MemberOrStaffEntity { }

    [Access(remove: "is:admin,owner")] // admin OR owner (owner degrades to authenticated at the gate)
    private sealed class AdminOrOwnerEntity { }

    [Access(remove: "owner")]
    private sealed class OwnerOnlyEntity { }

    [Access(write: "has:scope:orders:write")]
    private sealed class ScopeWriteEntity { }

    [Access(read: "has:claim:tier=pro")]
    private sealed class ClaimReadEntity { }

    [Access(read: "has:role:auditor")]
    private sealed class HasRoleEntity { }

    [Access(all: "is:admin")] // `all` → every action gated identically
    private sealed class AllGatedEntity { }

    // Multiple [Authorize(Roles=)] → AND across attributes, OR within (Grant.RoleAnyOf lowering).
    [Authorize(Roles = "a,b")]
    [Authorize(Roles = "c")]
    private sealed class MultiRoleEntity { }

    // Explicit [Access] (read) coexisting with legacy [Authorize] — explicit wins per-action, legacy fills the rest.
    [Access(read: "anyone")]
    [Authorize(Roles = "admin")]
    private sealed class MergedEntity { }

    // `all` as the default with an explicit per-action override.
    [Access(read: "anyone", all: "is:admin")]
    private sealed class AllWithOverrideEntity { }

    private static readonly EntityFloorAuthorizationProvider Provider = new(new AccessGateCache());

    private static Task<AuthorizeDecision?> Eval(Type entity, string action, ClaimsPrincipal user)
        => Provider.EvaluateAsync(new AuthorizeRequest { Subject = user, Action = action, Resource = entity });

    private static ClaimsPrincipal Anon() => new(new ClaimsIdentity());

    private static ClaimsPrincipal User(string[]? scopes = null, string[]? roles = null, (string Type, string Value)[]? claims = null)
    {
        var c = new List<Claim>();
        if (scopes is { Length: > 0 }) c.Add(new Claim("scope", string.Join(' ', scopes)));
        foreach (var r in roles ?? Array.Empty<string>()) c.Add(new Claim(ClaimTypes.Role, r));
        foreach (var (t, v) in claims ?? Array.Empty<(string, string)>()) c.Add(new Claim(t, v));
        return new ClaimsPrincipal(new ClaimsIdentity(c, authenticationType: "test"));
    }

    // ── Per-action granularity ───────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Unspecified_actions_stay_open_while_one_action_is_gated()
    {
        (await Eval(typeof(WriteOnlyGatedEntity), EntityAuthorizeActions.Read, Anon())).Should().BeNull("read is unspecified → open → defer");
        (await Eval(typeof(WriteOnlyGatedEntity), EntityAuthorizeActions.Remove, Anon())).Should().BeNull("remove is unspecified → open → defer");
        (await Eval(typeof(WriteOnlyGatedEntity), EntityAuthorizeActions.Write, Anon())).Should().BeOfType<AuthorizeDecision.Challenge>();
        (await Eval(typeof(WriteOnlyGatedEntity), EntityAuthorizeActions.Write, User())).Should().BeOfType<AuthorizeDecision.Allow>();
    }

    [Fact]
    public async Task Public_read_authed_write_admin_remove()
    {
        (await Eval(typeof(PublicReadEntity), EntityAuthorizeActions.Read, Anon())).Should().BeOfType<AuthorizeDecision.Allow>("read: anyone");
        (await Eval(typeof(PublicReadEntity), EntityAuthorizeActions.Write, Anon())).Should().BeOfType<AuthorizeDecision.Challenge>("write: authenticated, anon → 401");
        (await Eval(typeof(PublicReadEntity), EntityAuthorizeActions.Write, User())).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(PublicReadEntity), EntityAuthorizeActions.Remove, User(roles: new[] { "user" }))).Should().BeOfType<AuthorizeDecision.Forbid>("remove: is:admin, non-admin → 403");
        (await Eval(typeof(PublicReadEntity), EntityAuthorizeActions.Remove, User(roles: new[] { "admin" }))).Should().BeOfType<AuthorizeDecision.Allow>();
    }

    [Fact]
    public async Task All_param_gates_every_action()
    {
        (await Eval(typeof(AllGatedEntity), EntityAuthorizeActions.Read, User(roles: new[] { "admin" }))).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(AllGatedEntity), EntityAuthorizeActions.Write, User(roles: new[] { "user" }))).Should().BeOfType<AuthorizeDecision.Forbid>();
        (await Eval(typeof(AllGatedEntity), EntityAuthorizeActions.Remove, Anon())).Should().BeOfType<AuthorizeDecision.Challenge>();
    }

    // ── DNF / OR-of-bags ─────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Or_of_role_bags()
    {
        (await Eval(typeof(MemberOrStaffEntity), EntityAuthorizeActions.Write, User(roles: new[] { "member" }))).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(MemberOrStaffEntity), EntityAuthorizeActions.Write, User(roles: new[] { "staff" }))).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(MemberOrStaffEntity), EntityAuthorizeActions.Write, User(roles: new[] { "other" }))).Should().BeOfType<AuthorizeDecision.Forbid>();
        (await Eval(typeof(MemberOrStaffEntity), EntityAuthorizeActions.Write, Anon())).Should().BeOfType<AuthorizeDecision.Challenge>();
    }

    [Fact]
    public async Task Multiple_authorize_roles_are_anded_across_attributes()
    {
        (await Eval(typeof(MultiRoleEntity), EntityAuthorizeActions.Write, User(roles: new[] { "a", "c" }))).Should().BeOfType<AuthorizeDecision.Allow>("(a or b) AND c");
        (await Eval(typeof(MultiRoleEntity), EntityAuthorizeActions.Write, User(roles: new[] { "b", "c" }))).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(MultiRoleEntity), EntityAuthorizeActions.Write, User(roles: new[] { "a" }))).Should().BeOfType<AuthorizeDecision.Forbid>("missing c");
        (await Eval(typeof(MultiRoleEntity), EntityAuthorizeActions.Write, User(roles: new[] { "c" }))).Should().BeOfType<AuthorizeDecision.Forbid>("missing a or b");
    }

    [Fact]
    public async Task Explicit_access_and_legacy_attributes_merge_by_precedence()
    {
        (await Eval(typeof(MergedEntity), EntityAuthorizeActions.Read, Anon())).Should().BeOfType<AuthorizeDecision.Allow>("explicit [Access] read:anyone wins");
        (await Eval(typeof(MergedEntity), EntityAuthorizeActions.Write, User(roles: new[] { "user" }))).Should().BeOfType<AuthorizeDecision.Forbid>("legacy [Authorize(Roles=admin)] fills write");
        (await Eval(typeof(MergedEntity), EntityAuthorizeActions.Write, User(roles: new[] { "admin" }))).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(MergedEntity), EntityAuthorizeActions.Remove, Anon())).Should().BeOfType<AuthorizeDecision.Challenge>("legacy fills remove → needs auth");
    }

    [Fact]
    public async Task All_default_with_explicit_per_action_override()
    {
        (await Eval(typeof(AllWithOverrideEntity), EntityAuthorizeActions.Read, Anon())).Should().BeOfType<AuthorizeDecision.Allow>("read override = anyone");
        (await Eval(typeof(AllWithOverrideEntity), EntityAuthorizeActions.Write, User(roles: new[] { "user" }))).Should().BeOfType<AuthorizeDecision.Forbid>("all = is:admin");
        (await Eval(typeof(AllWithOverrideEntity), EntityAuthorizeActions.Remove, User(roles: new[] { "admin" }))).Should().BeOfType<AuthorizeDecision.Allow>();
    }

    [Fact]
    public void A_degenerate_no_condition_bag_denies_rather_than_silently_allowing()
    {
        var bag = new AccessBag(Array.Empty<string>(), Array.Empty<Grant>(), RequiresOwner: false, Anyone: false, Authenticated: false);
        AccessGateEvaluator.Evaluate(new ActionGate(new[] { bag }), User(), ownerSatisfied: true)
            .Should().BeOfType<AuthorizeDecision.Forbid>("a bag with no positive condition must never silently allow");
    }

    // ── owner degrades to authenticated at the coarse gate ───────────────────────────────────────────────────────
    [Fact]
    public async Task Owner_term_degrades_to_authenticated_at_the_gate()
    {
        (await Eval(typeof(AdminOrOwnerEntity), EntityAuthorizeActions.Remove, User(roles: new[] { "admin" }))).Should().BeOfType<AuthorizeDecision.Allow>("admin bag");
        (await Eval(typeof(AdminOrOwnerEntity), EntityAuthorizeActions.Remove, User())).Should().BeOfType<AuthorizeDecision.Allow>("owner degrades to authenticated → any signed-in passes the gate");
        (await Eval(typeof(AdminOrOwnerEntity), EntityAuthorizeActions.Remove, Anon())).Should().BeOfType<AuthorizeDecision.Challenge>("anonymous can own nothing");
        (await Eval(typeof(OwnerOnlyEntity), EntityAuthorizeActions.Remove, User())).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(OwnerOnlyEntity), EntityAuthorizeActions.Remove, Anon())).Should().BeOfType<AuthorizeDecision.Challenge>();
    }

    // ── grant kinds ──────────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scope_grant()
    {
        (await Eval(typeof(ScopeWriteEntity), EntityAuthorizeActions.Write, User(scopes: new[] { "orders:write" }))).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(ScopeWriteEntity), EntityAuthorizeActions.Write, User(scopes: new[] { "orders:read" }))).Should().BeOfType<AuthorizeDecision.Forbid>();
        (await Eval(typeof(ScopeWriteEntity), EntityAuthorizeActions.Write, Anon())).Should().BeOfType<AuthorizeDecision.Challenge>();
    }

    [Fact]
    public async Task Claim_grant()
    {
        (await Eval(typeof(ClaimReadEntity), EntityAuthorizeActions.Read, User(claims: new[] { ("tier", "pro") }))).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(ClaimReadEntity), EntityAuthorizeActions.Read, User(claims: new[] { ("tier", "free") }))).Should().BeOfType<AuthorizeDecision.Forbid>();
    }

    [Fact]
    public async Task Has_role_grant()
    {
        (await Eval(typeof(HasRoleEntity), EntityAuthorizeActions.Read, User(roles: new[] { "auditor" }))).Should().BeOfType<AuthorizeDecision.Allow>();
        (await Eval(typeof(HasRoleEntity), EntityAuthorizeActions.Read, User(roles: new[] { "other" }))).Should().BeOfType<AuthorizeDecision.Forbid>();
    }

    // ── typed-authoring parity (one model, two authoring surfaces) ───────────────────────────────────────────────
    [Fact]
    public void Typed_helpers_emit_the_canonical_string()
        => Access.Or(Access.Is("admin"), Access.Owner).Should().Be("is:admin, owner");

    [Fact]
    public void String_and_typed_forms_parse_to_the_same_structure()
    {
        var fromString = AccessGateParser.ParseValue("is:admin,owner", "X", "remove");
        var fromTyped = AccessGateParser.ParseValue(Access.Or(Access.Is("admin"), Access.Owner), "X", "remove");

        foreach (var gate in new[] { fromString, fromTyped })
        {
            gate.AnyOf.Should().HaveCount(2);
            gate.AnyOf[0].IsRolesAnyOf.Should().ContainSingle().Which.Should().Be("admin");
            gate.AnyOf[1].RequiresOwner.Should().BeTrue();
        }
    }

    // ── fail-fast parser ─────────────────────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("admin", "did you mean 'is:admin'")]
    [InlineData("is:", "empty role in 'is:'")]
    [InlineData("has:foo", "unknown grant")]
    [InlineData("has:claim:dept", "claim grant needs key=value")]
    [InlineData("is:member has:scope:x", "single-term OR-lists")]
    public void Malformed_values_fail_fast(string value, string expectedFragment)
    {
        var act = () => AccessGateParser.ParseValue(value, "Order", "write");
        act.Should().Throw<AccessGateException>().WithMessage($"*{expectedFragment}*");
    }

    // ── owner-inert detection (the boot-warning input) ───────────────────────────────────────────────────────────
    [Fact]
    public void Compile_flags_owner_declaration()
    {
        AccessGateCache.DeclaresOwner(AccessGateCache.Compile(typeof(OwnerOnlyEntity))).Should().BeTrue();
        AccessGateCache.DeclaresOwner(AccessGateCache.Compile(typeof(PublicReadEntity))).Should().BeFalse();
    }

    [Fact]
    public void Owner_declaration_logs_an_inert_warning_until_constrain_ships()
    {
        var logger = new CapturingLogger<AccessGateCache>();
        new AccessGateCache(logger).GetOrCompile(typeof(OwnerOnlyEntity));
        logger.Messages.Should().ContainSingle().Which
            .Should().Contain("owner").And.Contain("degrades to 'authenticated'");
    }

    [Fact]
    public void No_declaration_compiles_to_open()
        => AccessGateCache.Compile(typeof(AccessGateTests)).Should().BeSameAs(AccessGate.Open);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
