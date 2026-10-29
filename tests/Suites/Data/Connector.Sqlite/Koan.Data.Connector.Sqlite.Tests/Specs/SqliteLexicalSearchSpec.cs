using System.Diagnostics;
using System.IO;
using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Core.Lexical;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0159: an entity's <c>[Lexical]</c> string properties are kept in an FTS5 external-content table whose triggers
/// run inside the row's own statement, and <c>Entity.Lexical.Search</c> answers a bounded, ranked page over it.
/// </summary>
public sealed class SqliteLexicalSearchSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    public sealed class Post : Entity<Post>
    {
        [Lexical] public string Title { get; set; } = "";
        [Lexical] public string Body { get; set; } = "";
        public string Author { get; set; } = "";
        public bool FailOnSave { get; set; }
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

    private static void FailFlaggedPosts() =>
        Post.Lifecycle.BeforeUpsert(context => context.Current.FailOnSave
            ? throw new InvalidOperationException("deliberate second-row failure")
            : EntityLifecycleResult.Proceed());

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
        await new Post { Title = "delta", Body = "another note about neither" }.Save();

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
        var tooWide = () => Post.Lexical.Search("pagination", s => s.Top(LexicalQuery.MaximumSize + 1));
        await tooWide.Should().ThrowAsync<ArgumentOutOfRangeException>();

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

        match.Snippet.Should().NotBeNull().And.Contain("[moonlight]");
        match.Snippet!.Replace("…", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().HaveCountLessThanOrEqualTo(6, "the window is six words");
    }

    [Fact]
    public async Task A_rebuild_indexes_rows_that_existed_before_the_declaration()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-rebuild"));
        for (var index = 0; index < 3; index++)
            await new LegacyNote { Title = $"legacy note {index}", Body = "written before the index" }.Save();

        var before = await IndexedNote.Lexical.Status();
        before.Exists.Should().BeTrue("first use provisions the index structures");
        before.Rows.Should().Be(3);
        before.Indexed.Should().Be(0);
        before.Current.Should().BeFalse();
        (await IndexedNote.Lexical.Search("legacy", s => s.Top(10))).Should().BeEmpty();

        (await IndexedNote.Lexical.Rebuild()).Should().Be(3);

        var after = await IndexedNote.Lexical.Status();
        after.Current.Should().BeTrue();
        after.Indexed.Should().Be(3);
        (await IndexedNote.Lexical.Search("legacy", s => s.Top(10))).Should().HaveCount(3);
    }

    [Fact]
    public async Task Readiness_reports_the_indexed_entity_its_provider_and_whether_the_index_is_current()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-readiness"));
        await new Post { Title = "ready", Body = "" }.Save();

        var status = await Post.Lexical.Status();
        status.Provider.Should().Be("sqlite");
        status.Fields.Should().Equal("Title", "Body");
        status.Language.Should().Be("english");
        status.Current.Should().BeTrue();

        var contributor = host.Services.GetServices<IHealthContributor>().Single(candidate => candidate.Name == "data:lexical");
        var report = await contributor.Check();
        Output.WriteLine(string.Join(Environment.NewLine, report.Data!.Select(pair => $"{pair.Key}={pair.Value}")));
        report.State.Should().Be(HealthState.Healthy);
        report.Data.Should().ContainKey(nameof(Post)).WhoseValue.As<string>().Should().StartWith("sqlite:current:");
        report.Data.Should().ContainKey("entities").WhoseValue.As<string>().Should().Contain(nameof(Post));
    }

    [Fact]
    public async Task Two_probes_in_quick_succession_measure_the_index_once()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-probe"));
        await new Post { Title = "probed", Body = "" }.Save();
        var contributor = (LexicalIndexHealthContributor)host.Services.GetServices<IHealthContributor>()
            .Single(candidate => candidate.Name == "data:lexical");

        var first = await contributor.Check();
        var measured = contributor.Measurements;
        var second = await contributor.Check();

        measured.Should().BeGreaterThan(0, "the first probe measures each declaring entity");
        contributor.Measurements.Should().Be(measured, "the second probe answers the first measurement");
        second.Data.Should().ContainKey("interval").WhoseValue.Should().Be("00:05:00");
        second.Data.Should().ContainKey("observedAt").WhoseValue.Should().Be(first.Data!["observedAt"]);
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

        await using var validating = await BootAsync(new Dictionary<string, string?> { ["Koan:Data:Sqlite:DdlPolicy"] = "Validate" });
        using var lease = Lease(partition);
        var note = await new IndexedNote { Title = "lands without an index", Body = "" }.Save();

        var search = () => IndexedNote.Lexical.Search("lands", s => s.Top(5));
        var refusal = (await search.Should().ThrowAsync<NotSupportedException>()).Which;
        refusal.Message.Should().Contain("LX_").And.Contain("__koan_lexical").And.Contain("Koan:Data:Sqlite:DdlPolicy=AutoCreate");
        var rebuild = () => IndexedNote.Lexical.Rebuild();
        await rebuild.Should().ThrowAsync<NotSupportedException>();
        (await IndexedNote.Get(note.Id))!.Title.Should().Be("lands without an index");
        (await IndexedNote.Lexical.Status()).Exists.Should().BeFalse();
    }

    [Fact]
    public async Task A_save_that_fails_inside_an_atomic_scope_before_commit_leaves_the_index_unchanged()
    {
        RequireBackingStore();
        await using var host = await BootAsync(FailFlaggedPosts);
        using var _ = Lease(NewPartition("lexical-atomic"));
        await new Post { Title = "settled", Body = "" }.Save();
        var good = new Post { Title = "quarantine notice", Body = "" };
        var bad = new Post { Title = "quarantine failure", Body = "", FailOnSave = true };

        using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
        {
            await good.Save();
            await bad.Save();
            var commit = () => EntityContext.Commit();
            await commit.Should().ThrowAsync<TransactionException>();
        }

        (await Post.Lexical.Search("quarantine", s => s.Top(10))).Should().BeEmpty("the first row's index entry rolled back with the row");
        var status = await Post.Lexical.Status();
        status.Rows.Should().Be(1);
        status.Indexed.Should().Be(1);
        status.Current.Should().BeTrue();
    }

    [Fact]
    public async Task Measured_a_two_term_query_over_one_hundred_thousand_rows()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("lexical-measure"));
        string[] words =
        [
            "copper", "lantern", "harbor", "quartz", "meadow", "cinder", "violet", "anchor", "basalt", "ember",
            "falcon", "glacier", "hollow", "ivory", "juniper", "kestrel", "lumen", "marble", "nectar", "orchid"
        ];
        var random = new Random(20261029);
        var rows = new List<Post>(5_000);
        var build = Stopwatch.StartNew();
        for (var index = 0; index < 100_000; index++)
        {
            rows.Add(new Post
            {
                Id = $"post-{index:D6}",
                Title = $"{words[random.Next(words.Length)]} {words[random.Next(words.Length)]}",
                Body = string.Join(' ', Enumerable.Range(0, 12).Select(__ => words[random.Next(words.Length)]))
            });
            if (rows.Count < 5_000) continue;
            await Post.UpsertMany(rows);
            rows.Clear();
        }
        build.Stop();

        var cold = Stopwatch.StartNew();
        var first = await Post.Lexical.Search("copper lantern", s => s.Top(20));
        cold.Stop();
        var warm = Stopwatch.StartNew();
        var again = await Post.Lexical.Search("copper lantern", s => s.Top(20));
        warm.Stop();
        var status = await Post.Lexical.Status();

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder(Fixture.ConnectionString) { Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT sqlite_version() || ' fts5=' || (SELECT count(*) FROM pragma_compile_options WHERE compile_options = 'ENABLE_FTS5')";
        var engine = (string?)await version.ExecuteScalarAsync();

        Output.WriteLine($"lexical.measurement rows={status.Rows} indexed={status.Indexed} current={status.Current}");
        Output.WriteLine($"lexical.measurement build-ms={build.ElapsedMilliseconds} first-page-ms={cold.ElapsedMilliseconds} second-page-ms={warm.ElapsedMilliseconds} matches={first.Count}/{again.Count}");
        Output.WriteLine($"lexical.measurement engine={engine} file-system={new DriveInfo(Path.GetPathRoot(Fixture.DatabasePath)!).DriveFormat} database={Fixture.DatabasePath}");
        first.Should().HaveCount(20);
        status.Current.Should().BeTrue();
    }
}
