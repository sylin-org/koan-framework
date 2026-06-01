using System;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Connector.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Filtering;

/// <summary>
/// Unit (no live Mongo) wire-shape regression for DATA-XXXX. A predicate on a Guid-shaped string
/// field must render as a TOP-LEVEL BinData, not a <c>{_v: ...}</c> discriminator envelope. The
/// envelope came from emitting the value through a <c>FieldDefinition&lt;TEntity, object&gt;</c> ->
/// ObjectSerializer, which wraps non-primitive BsonValues; the query then matched nothing even
/// though the stored value was correct BinData. The fix emits scalar comparisons as raw documents.
/// </summary>
public sealed class MongoFilterWireShapeSpec
{
    private sealed class Probe
    {
        public string Id { get; set; } = "";
        public string PackageId { get; set; } = "";
    }

    private static BsonDocument Render(FilterDefinition<Probe> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return filter.Render(new RenderArgs<Probe>(registry.GetSerializer<Probe>(), registry));
    }

    private static BsonDocument Translate(System.Linq.Expressions.Expression<Func<Probe, bool>> predicate)
        => Render(new MongoFilterTranslator<Probe>(n => n).Translate(LinqFilterCompiler.Compile(predicate), typeof(Probe)));

    [Fact]
    public void Eq_on_guid_shaped_string_emits_top_level_binary()
    {
        var id = Guid.NewGuid().ToString();

        var doc = Translate(p => p.PackageId == id);

        doc.Contains("PackageId").Should().BeTrue();
        var value = doc["PackageId"];
        value.BsonType.Should().Be(BsonType.Binary);                  // NOT a {_v: ...} sub-document
        value.AsBsonBinaryData.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        value.AsBsonBinaryData.ToGuid().Should().Be(Guid.Parse(id));
    }

    [Fact]
    public void Eq_on_plain_string_stays_a_bson_string()
    {
        var doc = Translate(p => p.PackageId == "not-a-guid");

        doc["PackageId"].BsonType.Should().Be(BsonType.String);
        doc["PackageId"].AsString.Should().Be("not-a-guid");
    }
}
