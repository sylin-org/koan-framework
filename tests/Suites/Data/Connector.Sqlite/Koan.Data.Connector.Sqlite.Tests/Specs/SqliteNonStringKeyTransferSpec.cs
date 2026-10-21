using Koan.Core;
using Koan.Data.Core.Transfers;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-016. Only string identifiers streamed in provider-bounded pages, and a transfer always read through the stream
/// when the provider advertised bounded paging. A Guid- or int-keyed Entity on SQLite therefore could not be copied,
/// moved or mirrored at all, and then only through a reported materialized read (DATA-0113). NK-179: a stream in
/// identity order continues after the last identity it read, in the store's own order of that key, so Guid and
/// integral keys now stream in bounded pages like string keys.
/// </summary>
[Collection(nameof(SqliteNonStringKeyTransferSpec))]
[CollectionDefinition(nameof(SqliteNonStringKeyTransferSpec), DisableParallelization = true)]
public sealed class SqliteNonStringKeyTransferSpec
{
    public sealed class Shipment : Entity<Shipment, Guid>
    {
        public string Label { get; set; } = "";
    }

    public sealed class Counter : Entity<Counter, int>
    {
        public string Label { get; set; } = "";
    }

    [Fact]
    public async Task Guid_keyed_entities_copy_move_and_mirror_through_a_bounded_stream()
    {
        await WithHost(async () =>
        {
            for (var i = 0; i < 3; i++)
                await Data<Shipment, Guid>.Upsert(new Shipment { Id = Guid.CreateVersion7(), Label = $"shipment-{i}" });

            var copy = await Data<Shipment, Guid>.Copy().To(partition: "copy").Batch(2).Run();
            var mirror = await Data<Shipment, Guid>.Mirror().To(partition: "mirror").Batch(2).Run();
            var move = await Data<Shipment, Guid>.Move().To(partition: "archive").Batch(2).Run();

            copy.CopiedCount.Should().Be(3);
            mirror.CopiedCount.Should().Be(3);
            move.CopiedCount.Should().Be(3);
            move.DeletedCount.Should().Be(3);
            copy.Warnings.Should().BeEmpty("the source is streamed, not materialized");
            (await Data<Shipment, Guid>.All()).Should().BeEmpty();
            using (EntityContext.Partition("archive"))
                (await Data<Shipment, Guid>.All()).Should().HaveCount(3);
        });
    }

    [Fact]
    public async Task Int_keyed_entities_move_between_partitions()
    {
        await WithHost(async () =>
        {
            for (var id = 1; id <= 3; id++)
                await Data<Counter, int>.Upsert(new Counter { Id = id, Label = $"counter-{id}" });

            var move = await Data<Counter, int>.Move().To(partition: "archive").Batch(2).Run();

            move.CopiedCount.Should().Be(3);
            move.DeletedCount.Should().Be(3);
            using (EntityContext.Partition("archive"))
                (await Data<Counter, int>.All()).Select(counter => counter.Id).Should().BeEquivalentTo([1, 2, 3]);
        });
    }

    private static async Task WithHost(Func<Task> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-keys-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSetting("Koan:Environment", "Test")
                .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
                .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
                .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={path}")
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();
            await body();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
