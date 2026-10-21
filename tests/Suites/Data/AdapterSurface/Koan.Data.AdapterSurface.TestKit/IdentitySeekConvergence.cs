using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

public sealed class SeekText : Entity<SeekText>
{
    public string Label { get; set; } = "";
}

public sealed class SeekGuid : Entity<SeekGuid, Guid>
{
    public string Label { get; set; } = "";
}

public sealed class SeekInt : Entity<SeekInt, int>
{
    public string Label { get; set; } = "";
}

public sealed class SeekLong : Entity<SeekLong, long>
{
    public string Label { get; set; } = "";
}

/// <summary>
/// Cross-adapter oracle for streams read in identity order (NK-179, NK-205, NK-207, NK-306).
///
/// <para>A stream ordered by the identity continues each page after the last identity it returned, and the store
/// both orders and compares that identity. So the oracle is the store itself: the stream must reproduce the order
/// one unpaged read of the same store returns, whatever that store's comparison is. Integer keys are chosen so a
/// store that keeps them as text (10 before 2) and one that keeps them as numbers both have something to get
/// wrong, and the long keys cross the Int32 range.</para>
///
/// <para>The write specs change the source between two pages, ahead of and behind the read. A numbered page is a
/// position, so two deletes behind it skip rows and two inserts behind it repeat one; a seek names a row, so a row
/// present for the whole read is returned exactly once.</para>
/// </summary>
public static class IdentitySeekConvergence
{
    private static readonly int[] IntKeys = [7, 1, 100, 2, 20, 10, 3, -5];
    private static readonly long[] LongKeys = [5_000_000_000L, 2L, 3_000_000_000L, -4_000_000_000L, 10L, 1L, 20L];
    private static readonly Guid[] GuidKeys =
    [
        Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
        Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"),
        Guid.Parse("e1a2b3c4-0000-4000-8000-000000000001"),
        Guid.Parse("00000000-0000-0000-0000-0000000000ff"),
        Guid.Parse("ff000000-0000-0000-0000-000000000000"),
        Guid.Parse("a0000000-0000-0000-c000-000000000000"),
        Guid.Parse("0000000a-0000-0000-0000-000000000000"),
    ];

    /// <summary>Every supported key type streams in the store's own identity order, both ways, losing nothing.</summary>
    public static async Task AssertKeyedStreamsFollowStoreOrderAsync()
    {
        await AssertFollows<SeekText, string>(["b", "a10", "a2", "C", "a", "B0", "c1"], id => new SeekText { Id = id });
        await AssertFollows<SeekGuid, Guid>(GuidKeys, id => new SeekGuid { Id = id });
        await AssertFollows<SeekInt, int>(IntKeys, id => new SeekInt { Id = id });
        await AssertFollows<SeekLong, long>(LongKeys, id => new SeekLong { Id = id });
    }

    /// <summary>Deletes behind the read and an insert ahead of it: nothing skipped, the deleted-ahead row absent.</summary>
    public static async Task AssertDeletesDuringReadSkipNothingAsync()
    {
        await SeedText(8);
        var seen = await ReadAcrossWrite(async () =>
        {
            await Data<SeekText, string>.Delete("k01");
            await Data<SeekText, string>.Delete("k02");
            await Data<SeekText, string>.Delete("k05");
            await new SeekText { Id = "k09" }.Save();
        });

        seen.Should().Equal(["k01", "k02", "k03", "k04", "k06", "k07", "k08", "k09"],
            "rows present for the whole read come back once, a row deleted ahead does not, and one inserted ahead does");
    }

    /// <summary>Inserts behind the read: nothing repeated, and the rows inserted behind are not read.</summary>
    public static async Task AssertInsertsDuringReadRepeatNothingAsync()
    {
        await SeedText(8);
        var seen = await ReadAcrossWrite(async () =>
        {
            await new SeekText { Id = "k00" }.Save();
            await new SeekText { Id = "k02a" }.Save();
            await new SeekText { Id = "k06a" }.Save();
        });

        seen.Should().Equal(["k01", "k02", "k03", "k04", "k05", "k06", "k06a", "k07", "k08"],
            "no row is read twice, rows inserted behind the read are not read, and one inserted ahead is");
    }

    /// <summary>Removing each row as it is streamed visits every row (NK-207).</summary>
    public static async Task AssertStreamAndRemoveVisitsEveryRowAsync()
    {
        await SeedText(25);
        var visited = new List<string>();
        await foreach (var row in Data<SeekText, string>.AllStream(batchSize: 10))
        {
            visited.Add(row.Id);
            await row.Remove();
        }

        visited.Should().HaveCount(25).And.OnlyHaveUniqueItems();
        (await Data<SeekText, string>.All()).Should().BeEmpty();
    }

    private static async Task AssertFollows<TEntity, TKey>(IReadOnlyList<TKey> keys, Func<TKey, TEntity> create)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        await Data<TEntity, TKey>.DeleteAll();
        await Data<TEntity, TKey>.UpsertMany(keys.Select(create).ToArray());

        foreach (var sort in new[] { "Id", "-Id" })
        {
            var storeOrder = (await Data<TEntity, TKey>.Page(1, 50, sort)).Select(static row => row.Id).ToArray();
            var streamed = new List<TKey>();
            await foreach (var row in Data<TEntity, TKey>.AllStream(sort, 2))
                streamed.Add(row.Id);

            storeOrder.Should().HaveCount(keys.Count);
            streamed.Should().Equal(storeOrder,
                $"a {typeof(TKey).Name}-keyed stream by '{sort}' must reproduce the order its store returns, two rows a page");
        }

        var unsorted = new List<TKey>();
        await foreach (var row in Data<TEntity, TKey>.AllStream(batchSize: 3))
            unsorted.Add(row.Id);
        unsorted.Should().Equal((await Data<TEntity, TKey>.Page(1, 50, "Id")).Select(static row => row.Id),
            "a stream with no order of its own reads in identity order");
    }

    private static async Task SeedText(int count)
    {
        await Data<SeekText, string>.DeleteAll();
        await Data<SeekText, string>.UpsertMany(Enumerable.Range(1, count)
            .Select(index => new SeekText { Id = $"k{index:00}" }).ToArray());
    }

    // Reads the first page of three, lets the writer act, then drains the stream.
    private static async Task<List<string>> ReadAcrossWrite(Func<Task> write)
    {
        var seen = new List<string>();
        await using var rows = Data<SeekText, string>.AllStream(batchSize: 3).GetAsyncEnumerator();
        while (seen.Count < 3 && await rows.MoveNextAsync()) seen.Add(rows.Current.Id);
        await write();
        while (await rows.MoveNextAsync()) seen.Add(rows.Current.Id);
        return seen;
    }
}
