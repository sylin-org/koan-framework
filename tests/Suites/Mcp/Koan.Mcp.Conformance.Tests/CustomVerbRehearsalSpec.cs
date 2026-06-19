using System.Threading.Tasks;
using Koan.Mcp.TestKit;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN11 (docs/assessment/09 §14 — A10, the rehearsability gate) — a custom <c>[McpTool]</c> verb's effects
/// live in imperative code the framework cannot inspect. A <c>dry_run</c> on it must return an honest
/// PARTIAL rehearsal that names the limit and DOES NOT invoke the verb — never a silently/falsely-complete
/// dry-run. (A1's DB-rehearsable entity path is the complete-rehearsal case; this is its honest opposite.)
/// </summary>
public sealed class CustomVerbRehearsalSpec : IClassFixture<DryRunFixture>
{
    private readonly DryRunFixture _fx;

    public CustomVerbRehearsalSpec(DryRunFixture fx) => _fx = fx;

    [Fact]
    public async Task A_dry_run_on_a_custom_verb_does_not_execute_it()
    {
        var call = await _fx.CallToolAsync("widget_recompute", new JObject { ["id"] = "w-1", ["dry_run"] = true });

        var text = McpHarnessFixtureBase.ContentText(call) ?? "";
        text.Should().NotContain("recomputed:w-1", "a rehearsal must not invoke the un-inspectable verb");
        var diag = call["meta"]?["diagnostics"] as JObject;
        diag?["dryRun"]?.Value<bool>().Should().BeTrue();
        diag?["rehearsable"]?.Value<bool>().Should().BeFalse("the verb's effects are not framework-inspectable");
    }

    [Fact]
    public async Task The_same_verb_without_dry_run_executes_normally()
    {
        var call = await _fx.CallToolAsync("widget_recompute", new JObject { ["id"] = "w-2" });

        McpHarnessFixtureBase.ContentText(call).Should().Be("recomputed:w-2", "without dry_run the verb runs for real");
    }
}
