using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Core.Lexical;
using Koan.Tests.Data.Core.Support;

namespace Koan.Tests.Data.Core.Specs.Lexical;

/// <summary>
/// DATA-0159: a lexical page is bounded when it is built, not when a store runs it. A page whose offset would
/// overflow, and one that starts deeper than <see cref="LexicalQuery.MaximumOffset"/> matches, are refused by
/// <see cref="LexicalQuery"/> and by <see cref="LexicalSearchQuery.Page"/> with the bound named; the deepest allowed
/// page builds and runs on SQLite.
/// </summary>
public sealed class LexicalPageBoundSpec
{
    [DataAdapter("sqlite")]
    public sealed class Note : Koan.Data.Core.Model.Entity<Note>
    {
        [Lexical] public string Title { get; set; } = "";
    }

    [Theory]
    [InlineData(int.MaxValue, LexicalQuery.MaximumSize)]
    [InlineData(int.MaxValue, 1)]
    public void A_page_whose_offset_would_overflow_is_refused_at_construction(int page, int size)
    {
        var query = () => new LexicalQuery("bounded", page, size);
        query.Should().Throw<ArgumentOutOfRangeException>()
            .Which.Message.Should().Contain(LexicalQuery.MaximumOffset.ToString()).And.Contain("narrow the search");

        var declaration = () => new LexicalSearchQuery<Note>().Page(page, size);
        declaration.Should().Throw<ArgumentOutOfRangeException>()
            .Which.Message.Should().Contain(LexicalQuery.MaximumOffset.ToString());
    }

    [Fact]
    public void A_page_past_the_maximum_offset_is_refused_and_the_deepest_allowed_page_builds()
    {
        var deepest = LexicalQuery.MaximumOffset / LexicalQuery.MaximumSize + 1;

        var past = () => new LexicalQuery("bounded", deepest + 1, LexicalQuery.MaximumSize);
        past.Should().Throw<ArgumentOutOfRangeException>().Which.Message
            .Should().Contain(LexicalQuery.MaximumOffset.ToString())
            .And.Contain($"page {deepest + 1} of {LexicalQuery.MaximumSize}")
            .And.Contain($"no later than {deepest}");
        var declaration = () => new LexicalSearchQuery<Note>().Page(deepest + 1, LexicalQuery.MaximumSize);
        declaration.Should().Throw<ArgumentOutOfRangeException>();

        new LexicalQuery("bounded", deepest, LexicalQuery.MaximumSize).Offset.Should().Be(LexicalQuery.MaximumOffset);
        new LexicalQuery("bounded", LexicalQuery.MaximumOffset + 1, 1).Offset.Should().Be(LexicalQuery.MaximumOffset);
    }

    [Fact]
    public async Task The_deepest_allowed_page_runs_on_sqlite()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(includeSqlite: true);
        using var partition = runtime.UsePartition();
        await new Note { Title = "a bounded page" }.Save();

        var first = await Note.Lexical.Search("bounded", s => s.Top(5));
        first.Should().ContainSingle();

        var deepest = await Note.Lexical.Search("bounded", s => s.Page(LexicalQuery.MaximumOffset + 1, 1));
        deepest.Should().BeEmpty("the deepest allowed page starts past the only match");
    }
}
