using System.Text.Json;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Filtering;

/// <summary>
/// DATA-0162: a list against a JSON array column (HasAny, HasAll, HasNone) binds as one JSON text, so 2,050 or 70,000 values answer as two do.
/// </summary>
public sealed class SqlServerCollectionSetSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Theory(DisplayName = "SQL Server: HasAny, HasAll and HasNone over a large list answer as over a small one")]
    [InlineData(2)]
    [InlineData(2_050)]
    [InlineData(70_000)]
    public async Task Collection_lists_of_any_size(int size)
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("tags"));
        await new TaggedRow { Id = "r1", Tags = ["alpha", "beta"] }.Save();
        await new TaggedRow { Id = "r2", Tags = ["gamma"] }.Save();
        await new TaggedRow { Id = "r3", Tags = [] }.Save();

        var anyValues = new[] { "alpha" }.Concat(Pad(size - 1)).ToArray();
        var allValues = new[] { "alpha", "beta" }.Concat(Pad(Math.Max(0, size - 2))).ToArray();

        (await Ids("$in", anyValues)).Should().Equal("r1");
        (await Ids("$nin", anyValues)).Should().Equal("r2", "r3");
        (await Ids("$all", allValues)).Should().Equal(size == 2 ? ["r1"] : [], "no row holds the padding");
        (await Ids("$all", ["alpha", "beta"])).Should().Equal("r1");
    }

    private static async Task<string[]> Ids(string op, IReadOnlyList<string> values)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["Tags"] = new Dictionary<string, object> { [op] = values } });
        return (await TaggedRow.Query(json)).Select(static row => row.Id).Order().ToArray();
    }

    private static IEnumerable<string> Pad(int count) => Enumerable.Range(0, count).Select(static i => $"pad-{i}");

    public sealed class TaggedRow : Entity<TaggedRow>
    {
        public List<string> Tags { get; set; } = [];
    }
}
