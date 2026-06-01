using System;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Connector.Mongo;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Filtering;

/// <summary>
/// Unit (no live Mongo) wire-shape regression for DATA-0098. Encoding is selected per FIELD from
/// static metadata (IdentityEncoding), not by sniffing the value:
///  - a predicate on a declared GUID identity reference renders as a TOP-LEVEL BinData (never a
///    {_v: ...} discriminator envelope, which never matched the stored value);
///  - a guid-shaped value on a NON-identity string field stays a BSON string (no over-reach).
/// </summary>
public sealed class MongoFilterWireShapeSpec
{
    private sealed class Pkg : Entity<Pkg> { }                          // Entity<T> -> GUID identity

    private sealed class Sighting : Entity<Sighting>
    {
        [Parent(typeof(Pkg))] public string? PackageId { get; set; }   // GUID-encoded (ref to a GUID entity)
        public string Note { get; set; } = "";                         // plain string (not an id/ref)
    }

    private static BsonDocument Render(FilterDefinition<Sighting> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return filter.Render(new RenderArgs<Sighting>(registry.GetSerializer<Sighting>(), registry));
    }

    private static BsonDocument Translate(System.Linq.Expressions.Expression<Func<Sighting, bool>> predicate)
        => Render(new MongoFilterTranslator<Sighting>(n => n).Translate(LinqFilterCompiler.Compile(predicate), typeof(Sighting)));

    [Fact]
    public void Eq_on_a_guid_identity_reference_emits_top_level_binary()
    {
        var id = Guid.NewGuid().ToString();

        var doc = Translate(s => s.PackageId == id);

        var value = doc["PackageId"];
        value.BsonType.Should().Be(BsonType.Binary);                   // NOT a {_v: ...} sub-document
        value.AsBsonBinaryData.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        value.AsBsonBinaryData.ToGuid().Should().Be(Guid.Parse(id));
    }

    [Fact]
    public void Eq_on_a_non_identity_string_stays_a_bson_string()
    {
        var guidShaped = Guid.NewGuid().ToString();                    // looks like a guid, but Note is not an id/ref

        var doc = Translate(s => s.Note == guidShaped);

        doc["Note"].BsonType.Should().Be(BsonType.String);             // no over-reach
        doc["Note"].AsString.Should().Be(guidShaped);
    }
}
