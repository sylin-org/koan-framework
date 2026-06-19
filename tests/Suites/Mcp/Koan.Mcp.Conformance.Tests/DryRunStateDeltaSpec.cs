using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN11 (docs/assessment/09 §14 — A1/A2) — universal dry-run + state delta.
/// <para>T12: a <c>dry_run</c> mutation runs the full hook/validation pipeline, writes NOTHING, and returns
/// a prospective delta; the real run reports the SAME delta shape (rehearse → execute → same diff).</para>
/// <para>T13: a successful mutation returns a semantic delta of the committed field transitions.</para>
/// The delta honors walled-means-silent — an <c>[McpIgnore(Output)]</c> field never appears in it.
/// </summary>
public sealed class DryRunStateDeltaSpec : IClassFixture<DryRunFixture>
{
    private readonly DryRunFixture _fx;

    public DryRunStateDeltaSpec(DryRunFixture fx) => _fx = fx;

    private async Task<JToken> Upsert(JObject model, bool dryRun)
    {
        var tool = _fx.ResolveToolName("widget", EntityEndpointOperationKind.Upsert);
        var args = new JObject { ["model"] = model };
        if (dryRun) args["dry_run"] = true;
        return await _fx.CallToolAsync(tool, args);
    }

    private static JObject? Diagnostics(JToken call) => call["meta"]?["diagnostics"] as JObject;
    private static JObject? Delta(JToken call) => Diagnostics(call)?["delta"] as JObject;

    // Field names follow the MCP wire convention (PascalCase, DefaultContractResolver) — lower-case them so
    // the assertions read by data field, not by casing.
    private static string[] ChangedFields(JObject delta) =>
        (delta["changes"] as JArray)?.Select(c => c["field"]!.Value<string>()!.ToLowerInvariant()).ToArray() ?? [];

    [Fact]
    public async Task Dry_run_upsert_writes_nothing_but_returns_a_prospective_delta()
    {
        const string id = "an11-dry-create-1";
        var model = new JObject { ["id"] = id, ["title"] = "Alpha", ["color"] = "Green", ["stock"] = 5, ["internal"] = "secret" };

        var call = await Upsert(model, dryRun: true);

        Diagnostics(call)?["dryRun"]?.Value<bool>().Should().BeTrue("a dry-run posture is echoed into the result");
        var delta = Delta(call);
        delta.Should().NotBeNull("a dry-run returns the prospective delta");
        delta!["operation"]?.Value<string>().Should().Be("create", "the id does not exist yet");
        ChangedFields(delta).Should().Contain("stock").And.Contain("color");

        // The rehearsal committed nothing — an out-of-band REST read finds no row.
        var rest = _fx.CreateClient();
        var probe = await rest.GetAsync($"/api/widgets/{id}");
        probe.StatusCode.Should().Be(HttpStatusCode.NotFound, "a dry-run must not persist");
    }

    [Fact]
    public async Task Real_run_persists_and_reports_the_same_delta_shape_as_the_rehearsal()
    {
        const string id = "an11-real-create-1";
        var model = new JObject { ["id"] = id, ["title"] = "Beta", ["color"] = "Blue", ["stock"] = 9, ["internal"] = "secret" };

        var rehearsal = Delta(await Upsert((JObject)model.DeepClone(), dryRun: true));
        var committed = Delta(await Upsert((JObject)model.DeepClone(), dryRun: false));

        rehearsal.Should().NotBeNull();
        committed.Should().NotBeNull();
        committed!["operation"]?.Value<string>().Should().Be(rehearsal!["operation"]?.Value<string>(),
            "rehearse → execute → same diff");
        ChangedFields(committed).Should().BeEquivalentTo(ChangedFields(rehearsal),
            "the committed delta is shaped identically to the prospective one");

        var rest = _fx.CreateClient();
        (await rest.GetAsync($"/api/widgets/{id}")).StatusCode.Should().Be(HttpStatusCode.OK, "the real run persisted");
    }

    [Fact]
    public async Task State_delta_names_the_changed_field_on_an_update()
    {
        const string id = "an11-update-1";
        await Upsert(new JObject { ["id"] = id, ["title"] = "Gamma", ["color"] = "Red", ["stock"] = 1 }, dryRun: false);

        var call = await Upsert(new JObject { ["id"] = id, ["title"] = "Gamma", ["color"] = "Red", ["stock"] = 2 }, dryRun: false);

        var delta = Delta(call);
        delta!["operation"]?.Value<string>().Should().Be("update", "the row already exists");
        var stock = (delta["changes"] as JArray)!.OfType<JObject>()
            .Single(c => string.Equals(c["field"]!.Value<string>(), "stock", System.StringComparison.OrdinalIgnoreCase));
        stock["from"]?.Value<int>().Should().Be(1);
        stock["to"]?.Value<int>().Should().Be(2);
    }

    [Fact]
    public async Task Delta_never_leaks_an_output_ignored_field()
    {
        const string id = "an11-leak-1";
        var call = await Upsert(new JObject { ["id"] = id, ["title"] = "Delta", ["color"] = "Red", ["stock"] = 3, ["internal"] = "must-not-appear" }, dryRun: true);

        var delta = Delta(call);
        ChangedFields(delta!).Should().NotContain(f => string.Equals(f, "internal", System.StringComparison.OrdinalIgnoreCase),
            "an [McpIgnore(Output)] field is walled — absent from the delta, not redacted");
        // And its value must not appear anywhere in the delta payload.
        delta!.ToString().Should().NotContain("must-not-appear");
    }
}
