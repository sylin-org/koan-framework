using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// SQLite's LIKE folds ASCII case; the oracle compares ordinally (NK-022, NK-032). Scalar StartsWith, EndsWith and
/// Contains therefore bind the raw literal, so case is significant and LIKE metacharacters in the value are literal.
/// </summary>
public sealed class SqliteStringMatchSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    private static readonly StringMatchProbe[] Corpus =
    [
        new() { Id = "s1", Text = "Alpha" },
        new() { Id = "s2", Text = "a_b" },
        new() { Id = "s3", Text = "axb" },
        new() { Id = "s4", Text = "50%off" },
        new() { Id = "s5", Text = "back\\slash" },
        new() { Id = "s6", Text = "" },
        new() { Id = "s7", Text = null },
    ];

    [Theory]
    [InlineData("{ \"Text\": \"al*\" }")]
    [InlineData("{ \"Text\": \"Al*\" }")]
    [InlineData("{ \"Text\": \"*PHA\" }")]
    [InlineData("{ \"Text\": \"*pha\" }")]
    [InlineData("{ \"Text\": \"*LPH*\" }")]
    [InlineData("{ \"Text\": \"a_*\" }")]
    [InlineData("{ \"Text\": \"*%off\" }")]
    [InlineData("{ \"Text\": \"*\\\\s*\" }")]
    [InlineData("{ \"Text\": \"*alphabet\" }")]
    public async Task Scalar_string_matching_converges_with_the_ordinal_oracle(string json)
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        foreach (var existing in await Data<StringMatchProbe, string>.Query("{}")) await existing.Remove();
        await StringMatchProbe.UpsertMany(Corpus);

        var filter = JsonFilterParser.Parse<StringMatchProbe>(json);
        var oracle = Corpus.Where(InMemoryFilterEvaluator.Compile<StringMatchProbe>(filter))
            .Select(static row => row.Id).Order().ToArray();
        var actual = (await Data<StringMatchProbe, string>.Query(json))
            .Select(static row => row.Id).Order().ToArray();

        actual.Should().Equal(oracle, json);
    }

    public sealed class StringMatchProbe : Entity<StringMatchProbe, string>
    {
        public string? Text { get; set; }
    }
}
