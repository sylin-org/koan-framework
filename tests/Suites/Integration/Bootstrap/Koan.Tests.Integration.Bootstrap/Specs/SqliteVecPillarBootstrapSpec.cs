using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Boot-smoke for the durable in-process vector floor (per ARCH-0079). Proves the sqlite-vec connector is
/// discovered through real <c>AddKoan()</c>, loads its embedded native <c>vec0</c> extension, and performs a
/// real k-NN round-trip against a file-backed store — durable vectors with no server. The native binary is
/// embedded and self-extracted, so this exercises the exact load path a single-file publish would.
/// </summary>
public sealed class SqliteVecPillarBootstrapSpec
{
    [VectorAdapter("sqlitevec")]
    public sealed class SqliteVecDoc : Entity<SqliteVecDoc> { }

    [Fact]
    public async Task AddKoan_loads_vec0_and_ranks_durably()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"koan-vectest-{Guid.NewGuid():N}.db");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSetting("Koan:Environment", "Test")
                .WithSetting("Koan:Data:SqliteVec:ConnectionString", $"Data Source={dbPath}")
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();

            var repo = host.Services.GetRequiredService<IVectorService>().TryGetRepository<SqliteVecDoc, string>();
            repo.Should().NotBeNull("the sqlite-vec connector must be discovered + elected via [VectorAdapter]");

            await repo!.Upsert("east", new[] { 1f, 0f, 0f });
            await repo.Upsert("north", new[] { 0f, 1f, 0f });

            var result = await repo.Search(new VectorQueryOptions(new[] { 0.9f, 0.1f, 0f }, TopK: 2));
            result.Matches.Should().HaveCount(2);
            ((string)(object)result.Matches[0].Id).Should().Be("east", "vec0 cosine k-NN must rank the east vector first");

            // Persisted + retrievable by id (the durable differentiator over the in-memory floor).
            var roundTrip = await repo.GetEmbedding("east");
            roundTrip.Should().NotBeNull();
            roundTrip!.Length.Should().Be(3);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* best effort */ }
        }
    }
}
