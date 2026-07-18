using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Data.Core;
using Koan.Tenancy.Web.Authorization;
using Koan.Tenancy.Web.Controllers;
using Koan.Tenancy.Web.Services;
using Koan.Tenancy.Web.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Web.Tests;

public sealed class TenancyConsoleSpec
{
    private static readonly TenantAdministrationService Administration = new();
    private static IDisposable Iso() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    [Fact]
    public async Task Supported_administration_mutations_are_audited()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await Administration.CreateTenant("operator-1", "Acme", "acme");
        await Administration.RenameTenant("operator-1", tenant.Id, "Acme Corp");
        var membership = await Administration.GrantMembership(
            "operator-1", tenant.Id, "person-1", [TenancyRoles.Owner]);
        (await Administration.RevokeMembership("operator-1", membership!.Id)).Should().BeTrue();

        var audit = (await TenantAuditEntry.Query(entry => entry.TenantId == tenant.Id)).ToList();
        audit.Select(entry => entry.Action).Should().Contain([
            "tenant.created", "tenant.renamed", "membership.granted", "membership.revoked",
        ]);
    }

    [Fact]
    public async Task Tenant_codes_are_normalized_and_duplicate_codes_are_rejected()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var first = await Administration.CreateTenant("operator-1", "Acme", " ACME ");
        first.Code.Should().Be("acme");

        var act = async () => await Administration.CreateTenant("operator-1", "Other", "acme");
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
    }

    [Fact]
    public async Task Equivalent_membership_grants_converge_to_one_deterministic_seat()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await Administration.CreateTenant("operator-1", "Acme", null);
        var first = await Administration.GrantMembership(
            "operator-1", tenant.Id, "person-1", [TenancyRoles.Owner, TenancyRoles.Member]);
        var second = await Administration.GrantMembership(
            "operator-1", tenant.Id, "person-1", [TenancyRoles.Member, TenancyRoles.Owner]);

        second!.Id.Should().Be(first!.Id).And.Be(Membership.KeyFor(tenant.Id, "person-1"));
        (await Membership.Query(membership => membership.TenantId == tenant.Id)).Should().ContainSingle();
        second.Roles.Should().Equal(TenancyRoles.Owner, TenancyRoles.Member);
        (await TenantAuditEntry.Query(entry => entry.Action == "membership.granted")).Should().ContainSingle();
    }

    [Fact]
    public async Task Membership_grant_rejects_the_host_operator_role()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await Administration.CreateTenant("operator-1", "Acme", null);
        var act = async () => await Administration.GrantMembership(
            "operator-1", tenant.Id, "person-1", [TenancyRoles.Operator]);

        await act.Should().ThrowAsync<ArgumentException>();
        (await Membership.Query(membership => membership.TenantId == tenant.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Roster_projects_the_complete_registry_and_seat_counts()
    {
        await using var host = await ConsoleHostFixture.CreateAsync();
        host.ResetEntityCaches();
        using var _ = Iso();

        var tenant = await Administration.CreateTenant("operator-1", "Acme", "acme");
        await Administration.GrantMembership("operator-1", tenant.Id, "person-1", [TenancyRoles.Owner]);
        await Administration.GrantMembership("operator-1", tenant.Id, "person-2", [TenancyRoles.Member]);

        var runtime = (TenancyRuntime?)host.Services.GetService(typeof(TenancyRuntime))
                      ?? throw new InvalidOperationException("TenancyRuntime not registered.");
        var controller = new TenancyOperatorController(
            Administration,
            runtime,
            Options.Create(new TenancyConsoleOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Roster(default);
        var roster = (result.Result as OkObjectResult)!.Value.Should().BeOfType<TenantRoster>().Subject;
        roster.Posture.Should().Be("Closed");
        roster.Tenants.Single(row => row.Id == tenant.Id).SeatCount.Should().Be(2);
    }

    private static async Task<bool> Evaluate(
        TenancyPosture posture,
        ClaimsPrincipal user,
        TenancyConsoleOptions? options = null,
        HttpContext? http = null)
    {
        var environment = new StubEnvironment(
            posture == TenancyPosture.Open ? Environments.Development : Environments.Production);
        var runtime = new TenancyRuntime(Options.Create(new TenancyOptions()), environment);
        var accessor = new HttpContextAccessor { HttpContext = http };
        var handler = new OperatorAuthorizationHandler(
            runtime,
            Options.Create(options ?? new TenancyConsoleOptions()),
            accessor);
        var requirement = new OperatorRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Operator_gate_is_open_in_development()
        => (await Evaluate(TenancyPosture.Open, new ClaimsPrincipal(new ClaimsIdentity()))).Should().BeTrue();

    [Fact]
    public async Task Operator_gate_is_closed_without_a_grant()
        => (await Evaluate(TenancyPosture.Closed, new ClaimsPrincipal(new ClaimsIdentity()))).Should().BeFalse();

    [Fact]
    public async Task Operator_gate_accepts_the_host_role()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, TenancyRoles.Operator)],
            authenticationType: "test");
        (await Evaluate(TenancyPosture.Closed, new ClaimsPrincipal(identity))).Should().BeTrue();
    }

    [Fact]
    public async Task Operator_gate_accepts_a_configured_identity_only()
    {
        var options = new TenancyConsoleOptions { Grant = { Operators = ["operator-1"] } };
        var admitted = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "operator-1")], "test"));
        var denied = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "other")], "test"));

        (await Evaluate(TenancyPosture.Closed, admitted, options)).Should().BeTrue();
        (await Evaluate(TenancyPosture.Closed, denied, options)).Should().BeFalse();
    }

    [Fact]
    public async Task Development_admission_can_require_loopback()
    {
        var options = new TenancyConsoleOptions { RequireLoopbackForOpenPosture = true };
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var remote = new DefaultHttpContext();
        remote.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        var local = new DefaultHttpContext();
        local.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        (await Evaluate(TenancyPosture.Open, anonymous, options, remote)).Should().BeFalse();
        (await Evaluate(TenancyPosture.Open, anonymous, options, local)).Should().BeTrue();
    }

    private static async Task<(int Status, bool NextCalled)> RunExposure(
        string path,
        string host,
        TenancyConsoleOptions options,
        string? sendHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Host = new HostString(host);
        if (sendHeader is not null) context.Request.Headers[sendHeader] = "1";
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new Koan.Tenancy.Web.Hosting.TenancyConsoleExposureMiddleware(next);
        await middleware.InvokeAsync(context, Options.Create(options));
        return (context.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task Exposure_rejects_a_disallowed_host_with_404()
    {
        var options = new TenancyConsoleOptions { Exposure = { Hosts = ["ops.example.com"] } };
        (await RunExposure("/tenancy", "other.example.com", options)).Should().Be((404, false));
        (await RunExposure("/tenancy", "ops.example.com", options)).NextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Exposure_rejects_a_missing_required_header_with_404()
    {
        var options = new TenancyConsoleOptions { Exposure = { RequireHeader = "X-Koan-Console" } };
        (await RunExposure("/api/tenancy/admin/tenants", "any", options)).Should().Be((404, false));
        (await RunExposure(
            "/api/tenancy/admin/tenants", "any", options, "X-Koan-Console")).NextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Exposure_kill_switch_hides_only_console_paths()
    {
        var disabled = new TenancyConsoleOptions { Enabled = false };
        (await RunExposure("/tenancy", "any", disabled)).Should().Be((404, false));
        (await RunExposure("/health/live", "any", disabled)).NextCalled.Should().BeTrue();
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public StubEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
