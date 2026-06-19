using System;
using System.Security.Claims;
using Koan.Mcp.Execution;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// SEC-0004 Phase 3.3b — the MCP coarse-gate probe (<see cref="McpEntityGate"/>) is the entity-tool visibility
/// authority that replaced the per-transport <c>McpToolAccessPolicy</c> entity check. These unit specs pin the
/// two load-bearing behaviors: the 12-op → read/write/remove mapping mirrors the endpoint service's enforcement,
/// and a null principal is STDIO local-trust (unfiltered) while a concrete remote caller is gated per action.
/// </summary>
public sealed class McpEntityGateSpec
{
    // A per-action-gated type: read/write/remove each need a DISTINCT scope, so a wrong op→action mapping is
    // observable (a write-scoped caller must NOT see the read verb, and vice-versa).
    [Access(read: "has:scope:thing:read", write: "has:scope:thing:write", remove: "has:scope:thing:remove")]
    private sealed class GatedThing { }

    private static readonly IAccessGateCache Cache = new AccessGateCache();

    [Theory]
    [InlineData(EntityEndpointOperationKind.Upsert, EntityAuthorizeActions.Write)]
    [InlineData(EntityEndpointOperationKind.UpsertMany, EntityAuthorizeActions.Write)]
    [InlineData(EntityEndpointOperationKind.Patch, EntityAuthorizeActions.Write)]
    [InlineData(EntityEndpointOperationKind.Delete, EntityAuthorizeActions.Remove)]
    [InlineData(EntityEndpointOperationKind.DeleteMany, EntityAuthorizeActions.Remove)]
    [InlineData(EntityEndpointOperationKind.DeleteByQuery, EntityAuthorizeActions.Remove)]
    [InlineData(EntityEndpointOperationKind.DeleteAll, EntityAuthorizeActions.Remove)]
    [InlineData(EntityEndpointOperationKind.Collection, EntityAuthorizeActions.Read)]
    [InlineData(EntityEndpointOperationKind.Query, EntityAuthorizeActions.Read)]
    [InlineData(EntityEndpointOperationKind.GetById, EntityAuthorizeActions.Read)]
    [InlineData(EntityEndpointOperationKind.GetNew, EntityAuthorizeActions.Read)]
    public void Operation_maps_to_the_endpoint_action(EntityEndpointOperationKind op, string expected)
        => McpEntityGate.ActionFor(op).Should().Be(expected);

    [Fact]
    public void A_null_principal_is_local_trust_and_always_visible()
        => McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Delete, user: null)
            .Should().BeTrue("STDIO binds the raw handler with no principal — unfiltered by design");

    [Fact]
    public void An_anonymous_remote_caller_is_denied_every_verb()
    {
        var anon = new ClaimsPrincipal(new ClaimsIdentity());
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Collection, anon).Should().BeFalse();
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Upsert, anon).Should().BeFalse();
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Delete, anon).Should().BeFalse();
    }

    [Fact]
    public void A_scoped_caller_sees_only_the_verbs_its_scope_gates()
    {
        var reader = Scoped("thing:read");
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Collection, reader)
            .Should().BeTrue("the read scope gates the read verb");
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Upsert, reader)
            .Should().BeFalse("a read-only caller cannot see the write verb — proves write maps to its own action");
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Delete, reader)
            .Should().BeFalse("a read-only caller cannot see the remove verb");
    }

    [Fact]
    public void A_fully_scoped_caller_sees_every_verb()
    {
        var all = Scoped("thing:read", "thing:write", "thing:remove");
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Collection, all).Should().BeTrue();
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.Patch, all).Should().BeTrue();
        McpEntityGate.CoarseAllows(Cache, typeof(GatedThing), EntityEndpointOperationKind.DeleteAll, all).Should().BeTrue();
    }

    private static ClaimsPrincipal Scoped(params string[] scopes)
        => new(new ClaimsIdentity(new[] { new Claim("scope", string.Join(' ', scopes)) }, authenticationType: "test"));
}
