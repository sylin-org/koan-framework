using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Lexical;
using Koan.Tests.Data.Core.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Lexical;

/// <summary>
/// DATA-0159: an entity that declares <c>[Lexical]</c> properties is refused, before any write, on a route whose
/// provider does not declare <c>query.lexical</c>, with the provider and the corrections named; and the composition
/// facts say which entities declare a lexical index, over which fields and language, and which provider keeps it.
/// </summary>
public sealed class LexicalDeclarationSpec
{
    public sealed class Article : Koan.Data.Core.Model.Entity<Article>
    {
        [Lexical] public string Title { get; set; } = "";
    }

    [DataAdapter("sqlite")]
    public sealed class Memo : Koan.Data.Core.Model.Entity<Memo>
    {
        [Lexical] public string Title { get; set; } = "";
        [Lexical] public string Body { get; set; } = "";
    }

    [Theory]
    [InlineData("json")]
    [InlineData("inmemory")]
    public async Task A_provider_without_the_capability_refuses_the_first_write_naming_it(string adapter)
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        using var route = EntityContext.With(adapter: adapter, partition: $"lexical-{Guid.CreateVersion7():n}");
        var article = new Article { Title = "refused" };

        var save = () => article.Save();
        var refusal = (await save.Should().ThrowAsync<NotSupportedException>()).Which;
        refusal.Message.Should().Contain("query.lexical")
            .And.Contain(adapter)
            .And.Contain("[Lexical]")
            .And.Contain("SQLite and PostgreSQL");
        (await Article.Get(article.Id)).Should().BeNull("the refusal precedes any write");

        var search = () => Article.Lexical.Search("refused", s => s.Top(5));
        (await search.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("query.lexical");
    }

    [Fact]
    public async Task The_facts_name_the_indexed_entity_its_fields_its_language_and_its_provider()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(includeSqlite: true);

        var lockfile = KoanCompositionSnapshot.Build(
            runtime.Services, "spec", runtime.Services.GetRequiredService<IConfiguration>());

        lockfile.Capabilities.Should().ContainKey("data:lexical:memo")
            .WhoseValue.Should().Contain("provider:sqlite").And.Contain("fields:Title,Body").And.Contain("language:english");
        lockfile.Entities.Should().Contain(entity => entity.Type == nameof(Memo) && entity.Traits.Contains("Lexical"));
    }
}
