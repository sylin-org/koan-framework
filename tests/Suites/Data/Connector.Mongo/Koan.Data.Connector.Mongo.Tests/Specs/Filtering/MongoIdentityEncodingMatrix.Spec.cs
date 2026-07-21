using System;
using System.Linq;
using Koan.Data.Core.Relationships;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Filtering;

/// <summary>
/// Live conformance matrix for DATA-0098 (the identity-encoding codec). Against a real Mongo, it
/// proves the whole contract: GUID ids/refs round-trip and are queryable; slug (string-keyed) ids and
/// refs round-trip and are queryable; a guid-shaped string on a NON-identity field round-trips
/// verbatim (no over-reach, no canonicalization) and is queryable as a string; and an absent ref
/// returns empty (so delete-when-empty stays correct). The guid-ref case is the downstream consumer regression.
/// </summary>
public sealed class MongoIdentityEncodingMatrixSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task Identity_encoding_round_trips_and_queries_across_the_matrix()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("idmatrix");
        using var lease = Lease(partition);

        // --- GUID identity (Entity<T>): id round-trips and is queryable by predicate ---
        var pkg = await Package.Upsert(new Package { Label = "p1" });
        pkg.Id.Should().NotBeNullOrWhiteSpace();
        (await Package.Get(pkg.Id))!.Id.Should().Be(pkg.Id);                       // lossless round-trip
        (await Data<Package, string>.Query(p => p.Id == pkg.Id, partition)).Should().HaveCount(1);

        // --- Slug identity (Entity<T,string>): string id round-trips and is queryable ---
        await Catalog.Upsert(new Catalog { Id = "produce", Name = "Produce" });
        (await Catalog.Get("produce"))!.Id.Should().Be("produce");
        (await Data<Catalog, string>.Query(c => c.Id == "produce", partition)).Should().HaveCount(1);

        // A guid-shaped string on a NON-identity field must round-trip VERBATIM (no over-reach,
        // no canonicalization) — the old value-sniffer would have rewritten this to BinData and
        // returned a canonical lowercase string.
        var correlation = Guid.NewGuid().ToString().ToUpperInvariant();

        await Sighting.UpsertMany(new[]
        {
            new Sighting { PackageId = pkg.Id, CatalogId = "produce", CorrelationId = correlation, Status = Status.Published },
            new Sighting { PackageId = pkg.Id, CatalogId = "produce", CorrelationId = Guid.NewGuid().ToString(), Status = Status.Draft }
        });

        // --- GUID reference (the regression): query by a guid-shaped FK finds the rows ---
        (await Data<Sighting, string>.Query(s => s.PackageId == pkg.Id, partition)).Should().HaveCount(2);

        // --- Slug reference: query by a string FK finds the rows ---
        (await Data<Sighting, string>.Query(s => s.CatalogId == "produce", partition)).Should().HaveCount(2);

        // --- Enum: stored as a string (convention); a query by the enum value must match ---
        (await Data<Sighting, string>.Query(s => s.Status == Status.Published, partition)).Should().HaveCount(1);

        // --- Non-identity guid-shaped string: verbatim round-trip + queryable as a string ---
        var byCorrelation = await Data<Sighting, string>.Query(s => s.CorrelationId == correlation, partition);
        byCorrelation.Should().HaveCount(1);
        byCorrelation.Single().CorrelationId.Should().Be(correlation);             // uppercase preserved (no mutation)

        // --- Absent reference returns empty (delete-when-empty stays correct) ---
        (await Data<Sighting, string>.Query(s => s.PackageId == Guid.NewGuid().ToString(), partition)).Should().BeEmpty();
    }

    private sealed class Package : Entity<Package>
    {
        public string Label { get; set; } = "";
    }

    private sealed class Catalog : Entity<Catalog, string>
    {
        public string Name { get; set; } = "";
    }

    private enum Status { Draft, Published }

    private sealed class Sighting : Entity<Sighting>
    {
        [Parent(typeof(Package))] public string? PackageId { get; set; }   // GUID-encoded ref
        [Parent(typeof(Catalog))] public string? CatalogId { get; set; }   // string ref (slug-keyed target)
        public Status Status { get; set; }                                 // enum -> stored as a string (convention)
        public string CorrelationId { get; set; } = "";                    // plain guid-shaped string, NOT an id/ref
    }
}
