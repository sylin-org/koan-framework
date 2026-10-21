using System.Collections.Generic;
using System.Threading.Tasks;
using Koan.Data.AdapterSurface.TestKit;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>DATA-0150 on MongoDB: a unique index is keyed <c>{ __koan_tenant: 1, email: 1 }</c>.</summary>
public sealed class MongoUniqueIndexTenancySpec(MongoFixture fixture, ITestOutputHelper output)
    : UniqueIndexTenancySpecsBase<MongoFixture>(fixture, output)
{
    protected override async Task DropReportedIndex(IReadOnlyDictionary<string, object?> report)
    {
        var database = new MongoClient(Fixture.ConnectionString).GetDatabase(Fixture.Database);
        await database.GetCollection<BsonDocument>((string)report["Collection"]!)
            .Indexes.DropOneAsync((string)report["Index"]!);
    }
}
