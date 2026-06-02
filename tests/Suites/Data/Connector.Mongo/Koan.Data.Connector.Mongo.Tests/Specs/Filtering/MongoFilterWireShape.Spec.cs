using System;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Connector.Mongo;
using Koan.Data.Connector.Mongo.Initialization;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Filtering;

/// <summary>
/// Unit (no live Mongo) wire-shape regression for DATA-0098. The translator encodes a scalar
/// comparand by running it through the FIELD'S OWN registered serializer — never by re-deriving the
/// value's BSON form. This pins that contract for three representative serializers (the per-member
/// GUID codec, enum-as-string, and the default string serializer) by registering them on the test
/// entity exactly as the framework would at AddKoan time:
///  - a declared GUID identity reference renders top-level BinData (no {_v: ...} envelope);
///  - an enum renders a BSON string (matching EnumRepresentationConvention on the write side);
///  - a guid-shaped value on a non-identity string field stays a BSON string (no over-reach).
/// </summary>
public sealed class MongoFilterWireShapeSpec
{
    private enum Status { Draft, Published }

    private sealed class Pkg : Entity<Pkg> { }

    private sealed class Sighting : Entity<Sighting>
    {
        [Parent(typeof(Pkg))] public string? PackageId { get; set; }
        public Status Status { get; set; }
        public string Note { get; set; } = "";
    }

    static MongoFilterWireShapeSpec()
    {
        // Configure the member serializers exactly as the framework does: the per-member GUID codec on
        // the identity reference, and enum-as-string (the global convention). The translator must encode
        // comparands through THESE.
        if (!BsonClassMap.IsClassMapRegistered(typeof(Sighting)))
        {
            BsonClassMap.RegisterClassMap<Sighting>(cm =>
            {
                cm.AutoMap();
                cm.GetMemberMap(x => x.PackageId).SetSerializer(new SmartStringGuidSerializer());
                cm.GetMemberMap(x => x.Status).SetSerializer(new EnumSerializer<Status>(BsonType.String));
            });
        }
    }

    private static BsonDocument Render(FilterDefinition<Sighting> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return filter.Render(new RenderArgs<Sighting>(registry.GetSerializer<Sighting>(), registry));
    }

    private static BsonDocument Translate(System.Linq.Expressions.Expression<Func<Sighting, bool>> predicate)
        => Render(new MongoFilterTranslator<Sighting>(n => n).Translate(LinqFilterCompiler.Compile(predicate), typeof(Sighting)));

    [Fact]
    public void Guid_identity_reference_encodes_as_top_level_binary()
    {
        var id = Guid.NewGuid().ToString();

        var doc = Translate(s => s.PackageId == id);

        var value = doc["PackageId"];
        value.BsonType.Should().Be(BsonType.Binary);                       // NOT a {_v: ...} sub-document
        value.AsBsonBinaryData.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        value.AsBsonBinaryData.ToGuid().Should().Be(Guid.Parse(id));
    }

    [Fact]
    public void Enum_encodes_as_string_matching_the_write_convention()
    {
        var doc = Translate(s => s.Status == Status.Published);

        doc["Status"].BsonType.Should().Be(BsonType.String);               // not the numeric value 1
        doc["Status"].AsString.Should().Be("Published");
    }

    [Fact]
    public void Non_identity_string_stays_a_bson_string()
    {
        var guidShaped = Guid.NewGuid().ToString();

        var doc = Translate(s => s.Note == guidShaped);

        doc["Note"].BsonType.Should().Be(BsonType.String);                 // no over-reach
        doc["Note"].AsString.Should().Be(guidShaped);
    }
}
