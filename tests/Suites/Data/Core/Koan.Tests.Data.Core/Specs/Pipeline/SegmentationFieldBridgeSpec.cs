using AwesomeAssertions;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.KeyValue;
using Koan.Data.Core.Semantics;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

[Collection("managed-field-registry")]
public sealed class SegmentationFieldBridgeSpec : IDisposable
{
    private sealed class Note
    {
        public string Id { get; init; } = "note-1";
    }

    public SegmentationFieldBridgeSpec() => ManagedFieldRegistry.Reset();

    public void Dispose() => ManagedFieldRegistry.Reset();

    [Fact]
    public void Marked_segmentation_fields_are_evaluated_without_the_legacy_registry()
    {
        ManagedFieldRegistry.IsEmpty.Should().BeTrue();
        var filter = Filter.On(
            FieldPath.Managed("__koan_tenant", typeof(string)),
            FilterOperator.Eq,
            FilterValue.Of("tenant-a"));
        var evaluate = KvFilterEvaluator.Compile<Note>(filter);

        evaluate(new KvRecord<Note>(new Note(), new Dictionary<string, object?>
        {
            ["__koan_tenant"] = "tenant-a"
        })).Should().BeTrue();
        evaluate(new KvRecord<Note>(new Note(), new Dictionary<string, object?>
        {
            ["__koan_tenant"] = "tenant-b"
        })).Should().BeFalse();
    }

    [Fact]
    public void Json_reload_extracts_compiled_segmentation_fields_without_the_legacy_registry()
    {
        ManagedFieldRegistry.IsEmpty.Should().BeTrue();
        var json = JObject.Parse("""{"id":"note-1","__koan_tenant":"tenant-a"}""");

        var values = ManagedFieldJsonInjector.ExtractManaged(json, typeof(Note),
        [
            new DataSegmentationField("tenant", "__koan_tenant", typeof(string))
        ]);

        values.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, object?>("__koan_tenant", "tenant-a"));
    }

    [Fact]
    public void Hard_segmentation_cannot_claim_the_entity_family_type_field()
    {
        var builder = new SegmentationPlanBuilder();
        builder.ForOwner(new SemanticId("test")).Require(
            "type",
            () => SegmentationValue.For("anime"),
            appliesTo: null,
            "Establish a test scope.");
        var data = new DataSegmentationPlan(builder.Build());

        var act = () => data.For(typeof(Note));

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*reserved*{EntityFamilyStorage.TypeField}*");
    }

    [Fact]
    public void Json_injection_defensively_rejects_the_entity_family_type_field()
    {
        var act = () => ManagedFieldJsonInjector.InjectManaged(
            new JObject(),
            new Dictionary<string, object?>
            {
                [EntityFamilyStorage.TypeField] = "spoof"
            });

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*reserved*{EntityFamilyStorage.TypeField}*");
    }
}
