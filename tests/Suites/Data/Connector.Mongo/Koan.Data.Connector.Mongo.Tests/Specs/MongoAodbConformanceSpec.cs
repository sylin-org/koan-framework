using System.Collections.Generic;
using Koan.Data.Abstractions.Sources;
using Koan.Data.AdapterSurface.TestKit;
using MongoDB.Bson;
using MongoDB.Driver;
using KoanData = Koan.Data.Core.Data;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>
/// Mongo's AODB conformance ledger cell (ARCH-0103 §6 / P5) — the golden reference. Proves, through a real
/// <c>AddKoan()</c> boot over one Mongo container, that the Mongo repository realizes all three AODB
/// isolation modes AND declares the matching tokens. The two routed conformance sources share the one Mongo server but
/// live in distinct physical <b>databases</b> (the placement <c>MongoAdapterFactory</c> pools by connection+database).
/// </summary>
public sealed class MongoAodbConformanceSpec(MongoFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<MongoFixture>(fixture, output)
{
    [Fact(DisplayName = "D-01/bounded-container-listing/Adapter: MongoDB pages containers with opaque resumable bounds")]
    public async Task Inspection_lists_complete_and_resumable_bounded_pages()
    {
        RequireBackingStore();
        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 3; index++)
            expected.Add((await SeedAsync(new BsonDocument("ordinal", index))).Name);

        await using var host = await BootAsync();
        var inspector = KoanData.Source("Default").Inspect();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        var sawMore = false;
        var completed = false;

        for (var pageNumber = 0; pageNumber < 2048; pageNumber++)
        {
            var page = await inspector.Containers(1, continuation, TestContext.Current.CancellationToken);
            page.Containers.Should().HaveCountLessThanOrEqualTo(1);
            foreach (var container in page.Containers) seen.Add(container.Address.Name);

            if (page.Completion == StorageContainerPageCompletion.MoreAvailable)
            {
                sawMore = true;
                page.Continuation.Should().StartWith("koan-source-v1.");
                page.Continuation.Should().NotBe(continuation);
                continuation = page.Continuation;
                continue;
            }

            page.Completion.Should().Be(StorageContainerPageCompletion.Complete);
            page.Continuation.Should().BeNull();
            completed = true;
            break;
        }

        completed.Should().BeTrue("a bounded listing below MongoDB's safety ceiling must terminate as complete");
        sawMore.Should().BeTrue();
        expected.All(seen.Contains).Should().BeTrue();
    }

    [Fact(DisplayName = "D-02/source-bound-resolution/Adapter: MongoDB resolves safe addresses and rejects cross-source references")]
    public async Task Inspection_resolves_safe_source_bound_references()
    {
        RequireBackingStore();
        var seeded = await SeedAsync(new BsonDocument("kind", "resolution"));
        await using var host = await BootAsync(SourceSettings("inspection_other", DataSourceAccess.ReadOnly));

        var primary = KoanData.Source("Default").Inspect();
        var reference = await primary.Resolve(
            StorageAddress.From(Fixture.Database, seeded.Name),
            TestContext.Current.CancellationToken);

        reference.Source.Should().Be("Default");
        reference.Address.Namespace.Should().Equal(Fixture.Database);
        reference.Address.Name.Should().Be(seeded.Name);
        await primary.Describe(reference);

        await FluentActions.Awaiting(() => KoanData.Source("inspection_other").Inspect().Describe(
                reference,
                TestContext.Current.CancellationToken))
            .Should().ThrowAsync<StorageReferenceSourceMismatchException>();
    }

    [Fact(DisplayName = "D-03/honest-container-description/Adapter: MongoDB describes intrinsic traits and read-only effective operations without mutation")]
    public async Task Inspection_describes_policy_projected_container_truth_without_mutation()
    {
        RequireBackingStore();
        var seeded = await SeedAsync(new BsonDocument("kind", "description"));
        var before = await seeded.Collection.CountDocumentsAsync(
            FilterDefinition<BsonDocument>.Empty,
            cancellationToken: TestContext.Current.CancellationToken);
        await using var host = await BootAsync(SourceSettings("inspection_readonly", DataSourceAccess.ReadOnly));
        var inspector = KoanData.Source("inspection_readonly").Inspect();

        var reference = await inspector.Resolve(StorageAddress.From(seeded.Name), TestContext.Current.CancellationToken);
        var descriptor = await inspector.Describe(reference, TestContext.Current.CancellationToken);

        descriptor.ProviderKind.Should().Be("collection");
        descriptor.DisplayPath.Should().Be($"{Fixture.Database}/{seeded.Name}");
        descriptor.Traits.Should().HaveFlag(StorageContainerTraits.Records);
        descriptor.Traits.Should().HaveFlag(StorageContainerTraits.Physical);
        descriptor.Traits.Should().HaveFlag(StorageContainerTraits.ReadOnly);
        descriptor.EffectiveOperations.Should().HaveFlag(StorageContainerOperations.Describe);
        descriptor.EffectiveOperations.Should().HaveFlag(StorageContainerOperations.Sample);
        descriptor.EffectiveOperations.Should().NotHaveFlag(StorageContainerOperations.Write);
        descriptor.RecordShape.Should().BeNull("MongoDB collections do not promise one fixed document shape");
        (await seeded.Collection.CountDocumentsAsync(
            FilterDefinition<BsonDocument>.Empty,
            cancellationToken: TestContext.Current.CancellationToken)).Should().Be(before);
    }

    [Fact(DisplayName = "D-04/bounded-record-sampling/Adapter: MongoDB samples record containers without mutation and reports completion")]
    public async Task Inspection_samples_records_with_honest_bounds_and_completion()
    {
        RequireBackingStore();
        var seeded = await SeedAsync(
            new BsonDocument { ["kind"] = "sample", ["payload"] = new BsonDocument("rank", 1) },
            new BsonDocument { ["kind"] = "sample", ["payload"] = new BsonDocument("rank", 2) });
        await using var host = await BootAsync();
        var inspector = KoanData.Source("Default").Inspect();
        var reference = await inspector.Resolve(StorageAddress.From(seeded.Name), TestContext.Current.CancellationToken);

        var bounded = await inspector.Sample(reference, 1, TestContext.Current.CancellationToken);
        bounded.Records.Should().ContainSingle();
        bounded.Completion.Should().Be(RecordSetCompletion.ProviderLimit);
        bounded.Execution.EffectiveLimits.MaxRecords.Should().Be(1);
        bounded.Execution.AccountedBytes.Should().BeGreaterThan(0);

        var complete = await inspector.Sample(reference, 10, TestContext.Current.CancellationToken);
        complete.Records.Should().HaveCount(2);
        complete.Fields.Select(static field => field.Name).Should().Equal("_id", "kind", "payload");
        complete.Completion.Should().Be(RecordSetCompletion.Complete);
        (await seeded.Collection.CountDocumentsAsync(
            FilterDefinition<BsonDocument>.Empty,
            cancellationToken: TestContext.Current.CancellationToken)).Should().Be(2);

        await FluentActions.Awaiting(() => inspector.Sample(
                reference,
                0,
                TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "D-05/lossless-mongo-records/Adapter: MongoDB preserves ordered duplicate missing null and structured values")]
    public async Task Neutral_records_preserve_lossless_mongodb_values()
    {
        RequireBackingStore();
        var first = new BsonDocument { AllowDuplicateNames = true };
        first.Add("_id", 1);
        first.Add("duplicate", 11);
        first.Add("duplicate", 12);
        first.Add("explicitNull", BsonNull.Value);
        first.Add("binary", new BsonBinaryData(new byte[] { 1, 2, 3 }));
        first.Add("when", new BsonDateTime(new DateTime(2026, 7, 29, 12, 30, 0, DateTimeKind.Utc)));
        first.Add("number", new BsonDecimal128(12.5m));
        first.Add("document", new BsonDocument
        {
            ["enabled"] = true,
            ["items"] = new BsonArray { 1, "two" }
        });
        var second = new BsonDocument
        {
            ["_id"] = 2,
            ["duplicate"] = 21,
            ["onlySecond"] = "present"
        };
        var seeded = await SeedAsync(first, second);
        await using var host = await BootAsync();
        var inspector = KoanData.Source("Default").Inspect();
        var reference = await inspector.Resolve(StorageAddress.From(seeded.Name), TestContext.Current.CancellationToken);

        var records = await inspector.Sample(reference, 10, TestContext.Current.CancellationToken);

        records.Completion.Should().Be(RecordSetCompletion.Complete);
        records.Fields.Select(static field => field.Name).Should().Equal(
            "_id", "duplicate", "duplicate", "explicitNull", "binary", "when", "number", "document", "onlySecond");
        records.Records.Should().HaveCount(2);
        var materializedFirst = records.Records.Single(record => record.Get<int>(0) == 1);
        materializedFirst.FindOrdinals("duplicate").Should().Equal(1, 2);
        materializedFirst.Get<int>(1).Should().Be(11);
        materializedFirst.Get<int>(2).Should().Be(12);
        materializedFirst.TryGetValue(3, out var explicitNull).Should().BeTrue();
        explicitNull.Should().BeNull();
        materializedFirst.Get<byte[]>(4).Should().Equal(1, 2, 3);
        materializedFirst.Get<DateTime>(5).Kind.Should().Be(DateTimeKind.Utc);
        materializedFirst.Get<decimal>(6).Should().Be(12.5m);
        materializedFirst.Get<DataObject>(7).Properties.Should().HaveCount(2);

        var materializedSecond = records.Records.Single(record => record.Get<int>(0) == 2);
        materializedSecond.Get<int>(1).Should().Be(21);
        materializedSecond.TryGetValue(2, out _).Should().BeFalse("the second duplicate occurrence is missing");
        materializedSecond.TryGetValue(3, out _).Should().BeFalse("missing differs from explicit null");
    }

    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings()
    {
        var conn = Fixture.ConnectionString;
        return new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:conformance_a:Adapter"] = "mongo",
            ["Koan:Data:Sources:conformance_a:mongo:ConnectionString"] = conn,
            ["Koan:Data:Sources:conformance_a:mongo:Database"] = "koan_conf_a",
            ["Koan:Data:Sources:conformance_b:Adapter"] = "mongo",
            ["Koan:Data:Sources:conformance_b:mongo:ConnectionString"] = conn,
            ["Koan:Data:Sources:conformance_b:mongo:Database"] = "koan_conf_b",
        };
    }

    private IEnumerable<KeyValuePair<string, string?>> SourceSettings(string source, DataSourceAccess access) =>
        new Dictionary<string, string?>
        {
            [$"Koan:Data:Sources:{source}:Adapter"] = "mongo",
            [$"Koan:Data:Sources:{source}:mongo:ConnectionString"] = Fixture.ConnectionString,
            [$"Koan:Data:Sources:{source}:mongo:Database"] = Fixture.Database,
            [$"Koan:Data:Sources:{source}:Access"] = access.ToString()
        };

    private async Task<(string Name, IMongoCollection<BsonDocument> Collection)> SeedAsync(
        params BsonDocument[] documents)
    {
        var name = $"forge_{Guid.CreateVersion7():N}";
        var collection = new MongoClient(Fixture.ConnectionString)
            .GetDatabase(Fixture.Database)
            .GetCollection<BsonDocument>(name);
        await collection.InsertManyAsync(documents, cancellationToken: TestContext.Current.CancellationToken);
        return (name, collection);
    }
}
