using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Core.Lexical;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// DATA-0159: an entity's <c>[Lexical]</c> string properties are kept in a stored generated <c>tsvector</c> column
/// under a GIN index, computed by the server on every write, and <c>Entity.Lexical.Search</c> answers a bounded,
/// ranked page over it.
/// </summary>
public sealed class PostgresLexicalSearchSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    public sealed class Post : Entity<Post>
    {
        [Lexical] public string Title { get; set; } = "";
        [Lexical] public string Body { get; set; } = "";
        public string Author { get; set; } = "";
    }

    [Storage(Name = "KOAN_LEXICAL_LEGACY")]
    public sealed class LegacyNote : Entity<LegacyNote>
    {
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
    }

    [Storage(Name = "KOAN_LEXICAL_LEGACY")]
    public sealed class IndexedNote : Entity<IndexedNote>
    {
        [Lexical] public string Title { get; set; } = "";
        [Lexical] public string Body { get; set; } = "";
    }

    [Fact]
    public async Task The_index_is_built_on_save_and_a_term_from_each_field_finds_the_row()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-save"));
        var post = await new Post { Title = "Quantum gardening", Body = "Compost thrives under moonlight", Author = "zelda" }.Save();

        (await Post.Lexical.Search("gardening", s => s.Top(10))).Should().ContainSingle().Which.Id.Should().Be(post.Id);
        (await Post.Lexical.Search("moonlight", s => s.Top(10))).Should().ContainSingle().Which.Id.Should().Be(post.Id);
        (await Post.Lexical.Search("zelda", s => s.Top(10))).Should().BeEmpty("the author is not a declared field");
        (await Post.Lexical.Find("compost", s => s.Top(10))).Should().ContainSingle().Which.Title.Should().Be("Quantum gardening");

        // The GIN index exists under the derived name, so the search is served from the index and not a scan.
        await using var verify = new NpgsqlConnection(Fixture.ConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        await using var indexes = verify.CreateCommand();
        indexes.CommandText = "SELECT count(*) FROM pg_indexes WHERE indexname LIKE 'LX_%' AND indexdef LIKE '%USING gin%'";
        Convert.ToInt64(await indexes.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_two_term_query_ranks_the_row_holding_both_terms_above_the_row_holding_one()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-rank"));
        var both = await new Post { Title = "alpha beta", Body = "a note about both words" }.Save();
        var one = await new Post { Title = "alpha", Body = "a note about one word" }.Save();
        await new Post { Title = "gamma", Body = "a note about neither" }.Save();

        var matches = await Post.Lexical.Search("alpha beta", s => s.Top(10));

        matches.Select(static match => match.Id).Should().Equal(both.Id, one.Id);
        matches[0].Score.Should().BeGreaterThan(matches[1].Score);
    }

    [Fact]
    public async Task A_removed_row_leaves_the_results_and_an_edited_row_is_found_by_its_new_text_only()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-edit"));
        var removed = await new Post { Title = "velvet curtain", Body = "" }.Save();
        var edited = await new Post { Title = "ancient map", Body = "" }.Save();

        await removed.Remove();
        edited.Title = "modern chart";
        await edited.Save();

        (await Post.Lexical.Search("velvet", s => s.Top(10))).Should().BeEmpty();
        (await Post.Lexical.Search("ancient", s => s.Top(10))).Should().BeEmpty();
        (await Post.Lexical.Search("chart", s => s.Top(10))).Should().ContainSingle().Which.Id.Should().Be(edited.Id);
    }

    [Fact]
    public async Task A_text_read_as_typed_finds_the_word_it_ends_in_by_its_start_and_every_other_word_whole()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-typed"));
        var lanterns = await new Post { Title = "Copper lanterns", Body = "The glass is cut" }.Save();
        var streams = await new Post { Title = "Streams", Body = "Water keeps running" }.Save();

        (await Post.Lexical.Search("lant", s => s.Top(10))).Should().BeEmpty("a word is whole unless the text is read as typed");
        (await Post.Lexical.Search("lant", s => s.Top(10).AsTyped())).Should().ContainSingle().Which.Id.Should().Be(lanterns.Id);
        (await Post.Lexical.Find("gla", s => s.Top(10).AsTyped())).Should().ContainSingle().Which.Id.Should().Be(lanterns.Id);
        (await Post.Lexical.Search("lant streams", s => s.Top(10).AsTyped())).Should().ContainSingle().Which.Id.Should().Be(streams.Id);
        (await Post.Lexical.Search("lant ", s => s.Top(10).AsTyped())).Should().BeEmpty("a space ends the word");
        (await Post.Lexical.Search("lant", s => s.Top(1).AsTyped().Snippet(6, "[", "]"))).Should().ContainSingle()
            .Which.Snippet.Should().Contain("[lanterns]");

        (await Post.Lexical.Search("runnin", s => s.Top(10).AsTyped())).Should().BeEmpty("the stem of the partial word is not the start of 'run'");
        (await Post.Lexical.Search("running", s => s.Top(10).AsTyped())).Should().ContainSingle().Which.Id.Should().Be(streams.Id);
    }

    [Fact]
    public async Task A_search_within_a_predicate_ranks_and_pages_only_the_rows_it_holds_for()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-within"));
        for (var index = 0; index < 4; index++)
            await new Post { Title = "lantern lantern lantern", Author = "closed" }.Save();
        var open = new List<string>();
        for (var index = 0; index < 3; index++)
            open.Add((await new Post { Title = "a lantern among other words", Author = "open" }.Save()).Id);
        var allowed = new HashSet<string> { "open" };

        var first = await Post.Lexical.Search("lantern", s => s.Page(1, 2).Within(post => allowed.Contains(post.Author)));
        var second = await Post.Lexical.Search("lantern", s => s.Page(2, 2).Within(post => allowed.Contains(post.Author)));

        first.Should().HaveCount(2, "a page is full where such rows remain");
        second.Should().ContainSingle();
        first.Concat(second).Select(static match => match.Id).Should().BeEquivalentTo(open);
    }

    [Fact]
    public async Task Paging_is_bounded_and_two_pages_over_one_result_set_neither_overlap_nor_skip()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-page"));
        var ids = new List<string>();
        for (var index = 0; index < 7; index++)
            ids.Add((await new Post { Title = $"pagination row {index}", Body = "" }.Save()).Id);

        var unbounded = () => Post.Lexical.Search("pagination", s => { });
        await unbounded.Should().ThrowAsync<ArgumentException>().WithMessage("*bounded*");

        var first = await Post.Lexical.Search("pagination", s => s.Page(1, 3));
        var second = await Post.Lexical.Search("pagination", s => s.Page(2, 3));
        var third = await Post.Lexical.Search("pagination", s => s.Page(3, 3));

        first.Should().HaveCount(3);
        second.Should().HaveCount(3);
        third.Should().HaveCount(1);
        var seen = first.Concat(second).Concat(third).Select(static match => match.Id).ToArray();
        seen.Should().OnlyHaveUniqueItems("two pages over one result set do not overlap");
        seen.Should().BeEquivalentTo(ids, "the pages cover the result set");
    }

    [Fact]
    public async Task A_snippet_highlights_the_matched_terms_within_a_bounded_window()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-snippet"));
        await new Post
        {
            Title = "Field notes",
            Body = "The orchard slept through the afternoon while the river kept moving and only the heron noticed the moonlight on the water before dawn arrived"
        }.Save();

        var match = (await Post.Lexical.Search("moonlight", s => s.Top(1).Snippet(6, "[", "]"))).Should().ContainSingle().Which;

        Output.WriteLine(match.Snippet ?? "<no snippet>");
        match.Snippet.Should().NotBeNull().And.Contain("[moonlight]");
        match.Snippet!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().HaveCountLessThanOrEqualTo(6, "the window is six words");
    }

    [Fact]
    public async Task Rows_that_existed_before_the_declaration_are_indexed_and_a_rebuild_answers_the_count()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-rebuild"));
        for (var index = 0; index < 3; index++)
            await new LegacyNote { Title = $"legacy note {index}", Body = "written before the index" }.Save();

        // A stored generated column is computed by the server for every row the table already holds.
        var before = await IndexedNote.Lexical.Status();
        before.Exists.Should().BeTrue("first use provisions the column and its index");
        before.Rows.Should().Be(3);
        before.Current.Should().BeTrue();
        (await IndexedNote.Lexical.Rebuild()).Should().Be(3);
        (await IndexedNote.Lexical.Search("legacy", s => s.Top(10))).Should().HaveCount(3);
    }

    [Fact]
    public async Task Under_a_validate_policy_the_row_lands_and_the_search_refuses_naming_the_missing_index()
    {
        RequireBackingStore();
        var partition = NewPartition("lexical-validate");
        await using (var creating = await BootAsync())
        {
            using var _ = Lease(partition);
            await new LegacyNote { Title = "before the index", Body = "" }.Save();
        }

        await using var validating = await BootAsync(new Dictionary<string, string?> { ["Koan:Data:Postgres:DdlPolicy"] = "Validate" });
        using var lease = Lease(partition);
        var note = await new IndexedNote { Title = "lands without an index", Body = "" }.Save();

        var search = () => IndexedNote.Lexical.Search("lands", s => s.Top(5));
        var refusal = (await search.Should().ThrowAsync<NotSupportedException>()).Which;
        refusal.Message.Should().Contain("LX_").And.Contain("__koan_lexical").And.Contain("DdlPolicy=AutoCreate");
        var rebuild = () => IndexedNote.Lexical.Rebuild();
        await rebuild.Should().ThrowAsync<NotSupportedException>();
        (await IndexedNote.Get(note.Id))!.Title.Should().Be("lands without an index");
        (await IndexedNote.Lexical.Status()).Exists.Should().BeFalse();
    }

    [Fact]
    public async Task Readiness_reports_the_indexed_entity_its_provider_and_whether_the_index_is_current()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-readiness"));
        await new Post { Title = "ready", Body = "" }.Save();

        var status = await Post.Lexical.Status();
        status.Provider.Should().Be("postgres");
        status.Fields.Should().Equal("Title", "Body");
        status.Language.Should().Be("english");
        status.Current.Should().BeTrue();

        var contributor = host.Services.GetServices<IHealthContributor>().Single(candidate => candidate.Name == "data:lexical");
        var report = await contributor.Check();
        Output.WriteLine(string.Join(Environment.NewLine, report.Data!.Select(pair => $"{pair.Key}={pair.Value}")));
        report.State.Should().Be(HealthState.Healthy);
        report.Data.Should().ContainKey(nameof(Post)).WhoseValue.As<string>().Should().StartWith("postgres:current:");
    }
}
