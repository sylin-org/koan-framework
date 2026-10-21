using Koan.Core;
using Koan.Data.Core.Transfers;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-006 / NK-048 (DATA-0125). A transfer compares the physical places its endpoints reach, not the way they are
/// written. Every endpoint pair below differs as declared and is one SQLite table; before DATA-0125 a Move over any
/// of them upserted the rows onto themselves and then deleted them.
/// </summary>
[Collection(nameof(SqliteTransferPlaceSpec))]
[CollectionDefinition(nameof(SqliteTransferPlaceSpec), DisableParallelization = true)]
public sealed class SqliteTransferPlaceSpec
{
    private const int Rows = 3;

    public sealed class Parcel : Entity<Parcel>
    {
        public string Label { get; set; } = "";
    }

    [Fact]
    public Task Move_from_an_explicit_Default_to_an_unset_destination_is_refused()
        => AssertMoveRefused(seed: null, () => Parcel.Move().From(source: "Default").To().Run());

    [Fact]
    public Task Move_from_the_adapter_spelling_of_the_default_is_refused()
        => AssertMoveRefused(seed: null, () => Parcel.Move().From(adapter: "sqlite").To().Run());

    [Fact]
    public Task Move_between_two_spellings_of_one_source_is_refused()
        => AssertMoveRefused(seed: "Preview", () => Parcel.Move().From(source: "preview").To(source: "Preview").Run());

    [Fact]
    public Task Move_to_a_source_whose_connection_names_the_same_file_is_refused()
        => AssertMoveRefused(seed: null, () => Parcel.Move().To(source: "Alias").Run());

    [Fact]
    public Task Move_to_a_source_without_its_own_connection_is_refused()
        => AssertMoveRefused(seed: null, () => Parcel.Move().To(source: "Orphan").Run());

    [Fact]
    public async Task Move_whose_destination_inherits_the_ambient_partition_is_refused()
    {
        await using var host = await Start();
        using (EntityContext.Partition("hot"))
        {
            await Seed();
            var refusal = await Record.ExceptionAsync(() => Parcel.Move().From(partition: "hot").To().Run());
            (await Parcel.All()).Should().HaveCount(Rows, "a Move onto its own table must not delete anything");
            refusal.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain("refused").And.Contain("ambient EntityContext");
        }
    }

    [Fact]
    public async Task Move_between_partitions_that_differ_only_by_case_is_refused()
    {
        await using var host = await Start();
        using (EntityContext.Partition("Archive")) await Seed();

        var refusal = await Record.ExceptionAsync(
            () => Parcel.Move().From(partition: "Archive").To(partition: "archive").Run());

        using (EntityContext.Partition("Archive"))
            (await Parcel.All()).Should().HaveCount(Rows, "SQLite resolves both spellings to one table");
        refusal.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Mirror_onto_its_own_store_is_refused()
    {
        await using var host = await Start();
        await Seed();

        await FluentActions.Invoking(() => Parcel.Mirror().From(source: "Default").To().Run())
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Invoking(() => Parcel.Mirror(MirrorMode.Bidirectional).To(source: "Alias").Run())
            .Should().ThrowAsync<InvalidOperationException>();
        (await Parcel.All()).Should().HaveCount(Rows);
    }

    [Fact]
    public async Task Copy_onto_its_own_store_writes_nothing_and_says_so()
    {
        await using var host = await Start();
        await Seed();

        var result = await Parcel.Copy().To(source: "Alias").Run();

        result.ReadCount.Should().Be(0);
        result.CopiedCount.Should().Be(0);
        result.Warnings.Should().ContainSingle(warning => warning.Contains("was skipped", StringComparison.Ordinal));
        (await Parcel.All()).Should().HaveCount(Rows);
    }

    [Fact]
    public async Task Move_between_distinct_files_still_moves_and_drains_the_origin()
    {
        await using var host = await Start();
        await Seed();

        var result = await Parcel.Move().To(source: "Preview").Run();

        result.CopiedCount.Should().Be(Rows);
        result.DeletedCount.Should().Be(Rows);
        (await Parcel.All()).Should().BeEmpty();
        using (EntityContext.Source("Preview"))
            (await Parcel.All()).Should().HaveCount(Rows);
    }

    /// <summary>
    /// NK-305: two spellings that the text identity cannot join (a <c>\\?\</c> prefix on Windows, a directory link
    /// elsewhere) name one file. Before NK-305 the Move deleted every row it copied; now the adapter must prove the
    /// files apart, cannot, and the Move is refused.
    /// </summary>
    [Fact]
    public async Task Move_to_a_spelling_the_identity_cannot_join_is_refused_as_unproven()
    {
        await using var host = await Start();
        await Seed();

        var refusal = await Record.ExceptionAsync(() => Parcel.Move().To(source: "Spelled").Run());

        (await Parcel.All()).Should().HaveCount(Rows, "a Move onto its own file must not delete anything");
        refusal.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("could not prove");
    }

    [Fact]
    public async Task Move_between_two_existing_files_is_proven_apart_and_runs()
    {
        await using var host = await Start();
        using (EntityContext.Source("Preview"))
            await new Parcel { Label = "already-there" }.Save();
        await Seed();

        var result = await Parcel.Move().To(source: "Preview").Run();

        result.CopiedCount.Should().Be(Rows);
        (await Parcel.All()).Should().BeEmpty();
        using (EntityContext.Source("Preview"))
            (await Parcel.All()).Should().HaveCount(Rows + 1);
    }

    private async Task AssertMoveRefused(string? seed, Func<Task> move)
    {
        await using var host = await Start();
        using (seed is null ? null : EntityContext.Source(seed))
            await Seed();

        var refusal = await Record.ExceptionAsync(move);

        using (seed is null ? null : EntityContext.Source(seed))
            (await Parcel.All()).Should().HaveCount(Rows, "a Move onto its own table must not delete anything");
        refusal.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("would delete every row it copied");
    }

    private static async Task Seed()
    {
        for (var i = 0; i < Rows; i++)
            await new Parcel { Label = $"parcel-{i}" }.Save();
    }

    private static async Task<TransferHost> Start()
    {
        var primary = TempDatabase("primary");
        var preview = TempDatabase("preview");
        var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
            .WithSetting("Koan:Data:Sqlite:ConnectionString", $"Data Source={primary}")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={primary}")
            .WithSetting("Koan:Data:Sources:Preview:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Preview:ConnectionString", $"Data Source={preview}")
            // The same file, spelled differently: another option, and the path's own letter case changed.
            .WithSetting("Koan:Data:Sources:Alias:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Alias:ConnectionString", $"Data Source={primary.ToUpperInvariant()};Pooling=False")
            .WithSetting("Koan:Data:Sources:Orphan:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Spelled:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Spelled:ConnectionString", $"Data Source={OtherSpelling(primary)}")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();
        return new TransferHost(host, primary, preview);
    }

    private static string OtherSpelling(string path)
    {
        if (OperatingSystem.IsWindows()) return @"\\?\" + path;
        var link = Path.Combine(Path.GetTempPath(), $"koan-sqlite-link-{Guid.CreateVersion7():n}");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(path)!);
        return Path.Combine(link, Path.GetFileName(path));
    }

    private static string TempDatabase(string label)
        => Path.Combine(Path.GetTempPath(), $"koan-sqlite-transfer-{label}-{Guid.CreateVersion7():n}.db");

    private sealed class TransferHost(IAsyncDisposable host, params string[] files) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await host.DisposeAsync();
            SqliteConnection.ClearAllPools();
            foreach (var file in files)
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
