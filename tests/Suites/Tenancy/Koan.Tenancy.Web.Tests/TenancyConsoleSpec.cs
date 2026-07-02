using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Data.Core;
using Koan.Tenancy;
using Koan.Tenancy.Web.Authorization;
using Koan.Tenancy.Web.Controllers;
using Koan.Tenancy.Web.Operations;
using Koan.Tenancy.Web.Services;
using Koan.Tenancy.Web.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Web.Tests;

/// <summary>
/// ARCH-0104 — the tenancy control-plane operator console, proven through a real <c>AddKoan()</c> boot (ARCH-0079):
/// the roster projection, audited-by-construction lifecycle actions, the last-owner guard, the control-plane erase
/// operation, and the posture-aware fail-closed operator gate.
/// </summary>
public sealed class TenancyConsoleSpec
{
    private static IDisposable Iso() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    private static readonly TenantLifecycleService Lifecycle = new();

    [Fact]
    public async Task Erase_removes_control_plane_rows_records_counts_and_audits()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await new TenantRecord { Name = "Doomed" }.Save();
        await new Membership { Id = Membership.KeyFor(tenant.Id, "a@x.dev"), TenantId = tenant.Id, IdentityId = "a@x.dev", Roles = { TenancyRoles.Owner } }.Save();
        await new Membership { Id = Membership.KeyFor(tenant.Id, "b@x.dev"), TenantId = tenant.Id, IdentityId = "b@x.dev", Roles = { "member" } }.Save();
        await new Invite { TenantId = tenant.Id, Email = "c@x.dev", Role = "member", Token = "t", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) }.Save();

        var op = new TenantOperation { TenantId = tenant.Id, RequestedBy = "op@host" };
        await TenantOperation.EraseControlPlane(op);

        op.Status.Should().Be(TenantOperationStatus.Completed);
        op.RemovedMemberships.Should().Be(2);
        op.RemovedInvites.Should().Be(1);
        op.RemovedTenant.Should().BeTrue();

        (await TenantRecord.Get(tenant.Id)).Should().BeNull();
        (await Membership.Query(m => m.TenantId == tenant.Id)).Should().BeEmpty();
        (await Invite.Query(i => i.TenantId == tenant.Id)).Should().BeEmpty();

        (await TenantAuditEntry.All()).Should().Contain(e => e.Action == "tenant.erased" && e.TenantId == tenant.Id && e.Actor == "op@host");
    }

    [Fact]
    public async Task Erase_is_idempotent_on_an_already_absent_tenant()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var op = new TenantOperation { TenantId = "never-existed", RequestedBy = "op@host" };
        await TenantOperation.EraseControlPlane(op);

        op.Status.Should().Be(TenantOperationStatus.Completed);
        op.RemovedMemberships.Should().Be(0);
        op.RemovedInvites.Should().Be(0);
        op.RemovedTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Lifecycle_actions_are_audited_with_the_acting_operator()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await Lifecycle.CreateTenant("leo@host", "Acme", "acme");
        await Lifecycle.SetStatus("leo@host", tenant.Id, TenantStatus.Suspended);
        await Lifecycle.CreateInvite("leo@host", tenant.Id, "new@acme.dev", "member");

        var audit = (await TenantAuditEntry.All()).Where(e => e.TenantId == tenant.Id).ToList();
        audit.Should().Contain(e => e.Action == "tenant.created" && e.Actor == "leo@host");
        audit.Should().Contain(e => e.Action == "tenant.suspended" && e.Actor == "leo@host");
        audit.Should().Contain(e => e.Action == "invite.created" && e.Actor == "leo@host");

        (await TenantRecord.Get(tenant.Id))!.Status.Should().Be(TenantStatus.Suspended);
    }

    [Fact]
    public async Task RevokeMembership_refuses_the_last_owner_of_an_active_tenant()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await new TenantRecord { Name = "Solo" }.Save();
        var owner = await new Membership { Id = Membership.KeyFor(tenant.Id, "only@solo.dev"), TenantId = tenant.Id, IdentityId = "only@solo.dev", Roles = { TenancyRoles.Owner } }.Save();

        var result = await Lifecycle.RevokeMembership("op@host", owner.Id);

        result.Removed.Should().BeFalse();
        result.Reason.Should().Contain("last owner");
        (await Membership.Get(owner.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeMembership_removes_a_non_owner_and_audits()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await new TenantRecord { Name = "Team" }.Save();
        await new Membership { Id = Membership.KeyFor(tenant.Id, "owner@team.dev"), TenantId = tenant.Id, IdentityId = "owner@team.dev", Roles = { TenancyRoles.Owner } }.Save();
        var member = await new Membership { Id = Membership.KeyFor(tenant.Id, "member@team.dev"), TenantId = tenant.Id, IdentityId = "member@team.dev", Roles = { "member" } }.Save();

        var result = await Lifecycle.RevokeMembership("op@host", member.Id);

        result.Removed.Should().BeTrue();
        (await Membership.Get(member.Id)).Should().BeNull();
        (await TenantAuditEntry.All()).Should().Contain(e => e.Action == "membership.revoked" && e.TenantId == tenant.Id);
    }

    [Fact]
    public async Task Roster_projects_seat_and_pending_invite_counts()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var acme = await new TenantRecord { Name = "Acme", Code = "acme" }.Save();
        await new Membership { Id = Membership.KeyFor(acme.Id, "a@acme.dev"), TenantId = acme.Id, IdentityId = "a@acme.dev", Roles = { TenancyRoles.Owner } }.Save();
        await new Membership { Id = Membership.KeyFor(acme.Id, "b@acme.dev"), TenantId = acme.Id, IdentityId = "b@acme.dev", Roles = { "member" } }.Save();
        await new Invite { TenantId = acme.Id, Email = "c@acme.dev", Role = "member", Token = "t1", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) }.Save();
        await new Invite { TenantId = acme.Id, Email = "d@acme.dev", Role = "member", Token = "t2", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) }.Save(); // expired → not pending

        var runtime = host.Services.GetService(typeof(TenancyRuntime)) as TenancyRuntime
                      ?? throw new InvalidOperationException("TenancyRuntime not registered");
        var controller = new TenancyOperatorController(Lifecycle, runtime)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Roster(default);
        var roster = (result.Result as OkObjectResult)!.Value as TenancyOperatorController.RosterDto;

        roster!.Posture.Should().Be("Closed"); // "Test" env is non-development
        var row = roster.Tenants.Single(t => t.Id == acme.Id);
        row.SeatCount.Should().Be(2);
        row.PendingInvites.Should().Be(1); // the expired invite is excluded
    }

    [Fact]
    public async Task CreateInvite_refuses_a_reserved_host_role()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await new TenantRecord { Name = "Acme" }.Save();
        var act = async () => await Lifecycle.CreateInvite("op@host", tenant.Id, "x@acme.dev", TenancyRoles.Operator);

        await act.Should().ThrowAsync<ArgumentException>();
        (await Invite.Query(i => i.TenantId == tenant.Id)).Should().BeEmpty(); // nothing persisted — no operator minted via invite
    }

    [Fact]
    public async Task Detail_invite_view_omits_the_bearer_token()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await new TenantRecord { Name = "Acme" }.Save();
        var invite = await Lifecycle.CreateInvite("op@host", tenant.Id, "x@acme.dev", "member");
        invite!.Token.Should().NotBeNullOrEmpty(); // the accept token exists server-side

        var runtime = host.Services.GetService(typeof(TenancyRuntime)) as TenancyRuntime ?? throw new InvalidOperationException();
        var controller = new TenancyOperatorController(Lifecycle, runtime)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var detail = ((await controller.Detail(tenant.Id, default)).Result as OkObjectResult)!.Value as TenancyOperatorController.TenantDetailDto;
        detail!.Invites.Single().Email.Should().Be("x@acme.dev");
        // Structural guarantee: the projection type has no Token property — the bearer credential cannot reach the browser.
        typeof(TenancyOperatorController.InviteViewDto).GetProperty("Token").Should().BeNull();
    }

    [Fact]
    public async Task Erase_job_dispatched_under_an_act_as_tenant_ambient_completes_via_the_worker()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        // No Iso() partition: the async job hop carries the tenant axis (ARCH-0100), not the test partition — so the
        // worker must run in the same (default) partition the entities live in. Each test owns its DB, so this is isolated.

        var lifecycle = host.Services.GetService(typeof(TenantLifecycleService)) as TenantLifecycleService
                        ?? throw new InvalidOperationException("TenantLifecycleService not registered");

        var tenant = await new TenantRecord { Name = "ActAsErase" }.Save();
        await new Membership { Id = Membership.KeyFor(tenant.Id, "o@a.dev"), TenantId = tenant.Id, IdentityId = "o@a.dev", Roles = { TenancyRoles.Owner } }.Save();

        TenantOperation op;
        using (Tenant.Use(tenant.Id)) // the operator is "acting as" the tenant at submit time — the finding-5 scenario
            op = await lifecycle.RequestErase("op@host", tenant.Id);

        // Drive the real Koan.Jobs worker to completion (bounded poll). The [HostScoped] work-item + control-plane
        // rows must load and erase even though the job ran under a restored NON-host tenant ambient.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && await TenantRecord.Get(tenant.Id) is not null)
            await Task.Delay(200);

        (await TenantRecord.Get(tenant.Id)).Should().BeNull("the [HostScoped] erase job loads + runs under a non-host ambient");
        var completed = await TenantOperation.Get(op.Id);
        completed!.Status.Should().Be(TenantOperationStatus.Completed);
        completed.RemovedMemberships.Should().Be(1);
    }

    // --- The posture-aware operator gate (fail-closed) — a focused unit over the handler ---

    private static async Task<bool> Evaluate(TenancyPosture posture, ClaimsPrincipal user, TenancyConsoleOptions? opts = null, HttpContext? http = null)
    {
        var env = new StubEnv(posture == TenancyPosture.Open ? Environments.Development : Environments.Production);
        var runtime = new TenancyRuntime(Options.Create(new TenancyOptions()), env);
        var accessor = new HttpContextAccessor { HttpContext = http };
        var handler = new OperatorAuthorizationHandler(runtime, Options.Create(opts ?? new TenancyConsoleOptions()), accessor);
        var requirement = new OperatorRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Operator_gate_is_open_in_dev_for_anyone()
        => (await Evaluate(TenancyPosture.Open, new ClaimsPrincipal(new ClaimsIdentity()))).Should().BeTrue();

    [Fact]
    public async Task Operator_gate_is_fail_closed_in_prod_without_the_role()
        => (await Evaluate(TenancyPosture.Closed, new ClaimsPrincipal(new ClaimsIdentity()))).Should().BeFalse();

    [Fact]
    public async Task Operator_gate_admits_the_explicit_host_role_in_prod()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, TenancyRoles.Operator) }, authenticationType: "test");
        (await Evaluate(TenancyPosture.Closed, new ClaimsPrincipal(identity))).Should().BeTrue();
    }

    [Fact]
    public async Task Operator_gate_admits_a_break_glass_allow_list_identity_in_prod()
    {
        var opts = new TenancyConsoleOptions { Grant = { Operators = new[] { "leo@sylin.org" } } };
        var authed = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "leo@sylin.org") }, "test"));
        (await Evaluate(TenancyPosture.Closed, authed, opts)).Should().BeTrue();

        // A different authenticated identity, not on the list and without the role, is still denied (fail-closed).
        var other = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "mallory@evil.com") }, "test"));
        (await Evaluate(TenancyPosture.Closed, other, opts)).Should().BeFalse();
    }

    [Fact]
    public async Task Operator_gate_open_bypass_can_be_restricted_to_loopback()
    {
        var opts = new TenancyConsoleOptions { RequireLoopbackForOpenPosture = true };
        var anon = new ClaimsPrincipal(new ClaimsIdentity());

        var remote = new DefaultHttpContext();
        remote.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7"); // public
        (await Evaluate(TenancyPosture.Open, anon, opts, remote)).Should().BeFalse();

        var local = new DefaultHttpContext();
        local.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        (await Evaluate(TenancyPosture.Open, anon, opts, local)).Should().BeTrue();
    }

    // --- The exposure layer (routing → 404), a focused unit over the middleware ---

    private static async Task<(int Status, bool NextCalled)> RunExposure(string path, string host, TenancyConsoleOptions opts, string? sendHeader = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Host = new HostString(host);
        if (sendHeader is not null) ctx.Request.Headers[sendHeader] = "1";
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var mw = new Koan.Tenancy.Web.Hosting.TenancyConsoleExposureMiddleware(next);
        await mw.InvokeAsync(ctx, Options.Create(opts));
        return (ctx.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task Exposure_404s_a_console_request_from_a_disallowed_host()
    {
        var opts = new TenancyConsoleOptions { Exposure = { Hosts = new[] { "ops.acme.com" } } };
        (await RunExposure("/tenancy", "evil.com", opts)).Should().Be((404, false));
        (await RunExposure("/tenancy", "ops.acme.com", opts)).NextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Exposure_404s_a_console_request_missing_the_required_header()
    {
        var opts = new TenancyConsoleOptions { Exposure = { RequireHeader = "X-Koan-Console" } };
        (await RunExposure("/api/tenancy/admin/tenants", "any", opts)).Should().Be((404, false));
        (await RunExposure("/api/tenancy/admin/tenants", "any", opts, sendHeader: "X-Koan-Console")).NextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Exposure_404s_everything_when_the_kill_switch_is_off_but_passes_non_console_paths()
    {
        var disabled = new TenancyConsoleOptions { Enabled = false };
        (await RunExposure("/tenancy", "any", disabled)).Should().Be((404, false));
        // A non-console path is never guarded, even with a restrictive exposure.
        var pinned = new TenancyConsoleOptions { Exposure = { Hosts = new[] { "ops.acme.com" } } };
        (await RunExposure("/healthz", "evil.com", pinned)).NextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Exposure_treats_a_blank_host_entry_as_any_host()
    {
        // A stray blank array slot must not silently 404 every host (report says "any"; middleware must agree).
        var opts = new TenancyConsoleOptions { Exposure = { Hosts = new[] { "" } } };
        (await RunExposure("/tenancy", "anything.example", opts)).NextCalled.Should().BeTrue();
    }

    private sealed class StubEnv : IHostEnvironment
    {
        public StubEnv(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
