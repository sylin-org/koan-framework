using System.Linq;
using System.Threading.Tasks;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN11 (docs/assessment/09 §14 — A3, invariant #16) — "did you mean?". A validation error projects a
/// correction from SCHEMA FACTS ONLY (enum members, required fields) — never from row data / counts /
/// which records exist. This is walled-means-silent (#6) re-applied to the error channel: the existence
/// leak the AN-leak fix closed on the read path must not reopen through validation errors.
/// </summary>
public sealed class DidYouMeanSpec : IClassFixture<DryRunFixture>
{
    private readonly DryRunFixture _fx;

    public DidYouMeanSpec(DryRunFixture fx) => _fx = fx;

    private async Task<JToken> Upsert(JObject model)
    {
        var tool = _fx.ResolveToolName("widget", EntityEndpointOperationKind.Upsert);
        return await _fx.CallToolAsync(tool, new JObject { ["model"] = model });
    }

    private static JArray? DidYouMean(JToken call) => call["meta"]?["diagnostics"]?["didYouMean"] as JArray;

    [Fact]
    public async Task An_invalid_enum_value_is_corrected_from_the_schema_members()
    {
        var call = await Upsert(new JObject { ["id"] = "an11-dym-enum", ["title"] = "X", ["color"] = "Purpel", ["stock"] = 1 });

        McpHarnessFixtureBase.IsError(call).Should().BeTrue("an unparseable enum value is a validation error");
        var issues = DidYouMean(call);
        issues.Should().NotBeNull("the error projects a schema-derived correction");
        var enumIssue = issues!.OfType<JObject>().Single(i => i["field"]!.Value<string>()!.Contains("color", System.StringComparison.OrdinalIgnoreCase));
        var valid = (enumIssue["validValues"] as JArray)!.Select(v => v.Value<string>()).ToArray();
        valid.Should().BeEquivalentTo(new[] { "Red", "Green", "Blue" }, "the correction is the enum's members");
    }

    [Fact]
    public async Task The_correction_is_schema_only_independent_of_any_row_data()
    {
        // No widgets of this colour exist (the store cannot be the source). The members come from the type.
        var call = await Upsert(new JObject { ["id"] = "an11-dym-schema", ["title"] = "X", ["color"] = "Mauve", ["stock"] = 1 });

        var issues = DidYouMean(call);
        var enumIssue = issues!.OfType<JObject>().Single(i => i["field"]!.Value<string>()!.Contains("color", System.StringComparison.OrdinalIgnoreCase));
        (enumIssue["validValues"] as JArray)!.Should().HaveCount(3, "the members are schema facts, not a row count");
        // The error channel reveals nothing about existence — no count/exists/total leaks.
        var diag = call["meta"]!["diagnostics"]!.ToString().ToLowerInvariant();
        diag.Should().NotContain("\"count\"").And.NotContain("\"exists\"").And.NotContain("\"total\"");
    }

    [Fact]
    public async Task A_missing_required_field_is_flagged_from_the_schema()
    {
        var call = await Upsert(new JObject { ["id"] = "an11-dym-required", ["color"] = "Red", ["stock"] = 1 });

        var issues = DidYouMean(call);
        issues.Should().NotBeNull();
        issues!.OfType<JObject>().Should().Contain(i =>
            i["field"]!.Value<string>()!.Contains("title", System.StringComparison.OrdinalIgnoreCase) && i["reason"]!.Value<string>() == "required",
            "a missing required field is a schema fact");
    }
}
