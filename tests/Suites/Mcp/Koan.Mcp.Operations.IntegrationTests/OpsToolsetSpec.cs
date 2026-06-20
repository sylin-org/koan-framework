using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Mcp.TestKit;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.Operations.IntegrationTests;

/// <summary>
/// P3.2 — the operational toolsets end-to-end through the real MCP handler (ARCH-0079): config-gated visibility, the
/// <c>@ops:</c> grant gate (fail-loud), the confirm/dry-run contract, the actual ledger effect, and the audit row.
/// </summary>
public sealed class OpsEnabledSpec : IClassFixture<OpsEnabledFixture>
{
    private readonly OpsEnabledFixture _fx;
    public OpsEnabledSpec(OpsEnabledFixture fx) => _fx = fx;

    private static ClaimsPrincipal Agent(string subject)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, subject) }, "Test"));

    [Fact]
    public async Task Ops_tools_are_listed_when_the_toolsets_are_enabled()
    {
        var tools = await _fx.ListToolsAsync();
        var names = tools.Select(t => t["name"]?.Value<string>()).ToList();
        names.Should().Contain("koan.jobs.trigger");
        names.Should().Contain("koan.cache.flush");
        names.Should().Contain("koan.cache.flushAll");
    }

    [Fact]
    public async Task Trigger_without_a_grant_fails_loud_naming_the_ops_grant()
    {
        var result = await _fx.CallToolAsAsync("koan.jobs.trigger",
            new JObject { ["workType"] = typeof(ImportJob).FullName, ["action"] = "import" },
            Agent("ungranted-agent"));

        McpHarnessFixtureBase.IsError(result).Should().BeTrue();
        McpHarnessFixtureBase.ContentText(result).Should().Contain("@ops:jobs");
    }

    [Fact]
    public async Task Trigger_with_a_grant_runs_against_the_ledger_and_audits()
    {
        await new AgentGrant { Subject = "jobs-agent", Resource = "@ops:jobs" }.Save();
        var workType = typeof(ImportJob).FullName!;

        var result = await _fx.CallToolAsAsync("koan.jobs.trigger",
            new JObject { ["workType"] = workType, ["action"] = "import" }, Agent("jobs-agent"));

        McpHarnessFixtureBase.IsError(result).Should().BeFalse(McpHarnessFixtureBase.ContentText(result));
        McpHarnessFixtureBase.ContentText(result).Should().Contain("jobId");

        // the trigger reached the REAL ledger (not a mock) — a job record exists for the work type.
        using var scope = _fx.Services.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IJobCoordinator>();
        var records = await coordinator.WhereAsync(new JobQuery(WorkType: workType), CancellationToken.None);
        records.Should().NotBeEmpty("koan.jobs.trigger submitted a job to the ledger");

        // the mutation was audited (SEC-0005 AgentAction).
        var audits = await AgentAction.Query(a => a.Subject == "jobs-agent");
        audits.Should().Contain(a => a.Action == "trigger" && a.Resource == "@ops:jobs");
    }

    [Fact]
    public async Task FlushAll_without_confirm_returns_a_dry_run_not_a_flush()
    {
        await new AgentGrant { Subject = "cache-agent", Resource = "@ops:cache" }.Save();

        var result = await _fx.CallToolAsAsync("koan.cache.flushAll", new JObject(), Agent("cache-agent"));

        McpHarnessFixtureBase.IsError(result).Should().BeFalse(McpHarnessFixtureBase.ContentText(result));
        McpHarnessFixtureBase.ContentText(result).Should().Contain("DRY RUN");
    }
}

/// <summary>P3.2 — a disabled operational toolset is ABSENT from the surface (the global config gate).</summary>
public sealed class OpsDisabledSpec : IClassFixture<OpsDisabledFixture>
{
    private readonly OpsDisabledFixture _fx;
    public OpsDisabledSpec(OpsDisabledFixture fx) => _fx = fx;

    [Fact]
    public async Task Ops_tools_are_absent_when_the_toolsets_are_disabled()
    {
        var tools = await _fx.ListToolsAsync();
        var names = tools.Select(t => t["name"]?.Value<string>()).ToList();
        names.Should().NotContain("koan.jobs.trigger");
        names.Should().NotContain("koan.cache.flush");
        names.Should().NotContain("koan.cache.flushAll");
    }
}
