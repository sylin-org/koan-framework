using System.Threading.Tasks;
using Koan.Mcp.TestKit;
using Koan.Web.Endpoints;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN4 (docs/assessment/09 §8 · AN-cards) — the spec <c>annotations</c> object (established by AN1) now
/// carries verb-derived hints. Entity verbs derive readOnly/destructive/idempotent mechanically from the
/// 12-op kind; custom <c>[McpTool]</c> verbs gain nothing automatically and must opt in with
/// <c>[McpReadOnly]</c>/<c>[McpDestructive]</c>/<c>[McpIdempotent]</c>.
/// </summary>
public sealed class AnnotationDerivationSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public AnnotationDerivationSpec(ConformanceFixture fx) => _fx = fx;

    private async Task<JObject> Annotations(string entity, EntityEndpointOperationKind op)
    {
        var name = _fx.ResolveToolName(entity, op);
        var tool = await _fx.GetWireToolAsync(name);
        tool.Should().NotBeNull();
        return (JObject)tool!["annotations"]!;
    }

    [Fact]
    public async Task Read_verbs_are_readOnly()
    {
        (await Annotations("gadget", EntityEndpointOperationKind.GetById))["readOnlyHint"]?.Value<bool>()
            .Should().BeTrue("get-by-id is a read");
        (await Annotations("gadget", EntityEndpointOperationKind.Query))["readOnlyHint"]?.Value<bool>()
            .Should().BeTrue("query is a read");
    }

    [Fact]
    public async Task Delete_is_destructive_and_not_readOnly()
    {
        var ann = await Annotations("gadget", EntityEndpointOperationKind.Delete);
        ann["readOnlyHint"]?.Value<bool>().Should().BeFalse();
        ann["destructiveHint"]?.Value<bool>().Should().BeTrue("delete is destructive");
    }

    [Fact]
    public async Task Upsert_is_idempotent_and_not_readOnly()
    {
        var ann = await Annotations("gadget", EntityEndpointOperationKind.Upsert);
        ann["readOnlyHint"]?.Value<bool>().Should().BeFalse();
        ann["idempotentHint"]?.Value<bool>().Should().BeTrue("upsert by id is idempotent");
    }

    [Fact]
    public async Task Custom_verb_emits_only_explicitly_marked_hints()
    {
        var unmarked = (JObject)(await _fx.GetWireToolAsync("gadget_ping"))!["annotations"]!;
        unmarked.ContainsKey("readOnlyHint").Should().BeFalse("an unmarked custom verb gains no hints automatically");
        unmarked.ContainsKey("destructiveHint").Should().BeFalse();

        var purge = (JObject)(await _fx.GetWireToolAsync("gadget_purge"))!["annotations"]!;
        purge["destructiveHint"]?.Value<bool>().Should().BeTrue("[McpDestructive] marks the verb destructive");
    }
}
