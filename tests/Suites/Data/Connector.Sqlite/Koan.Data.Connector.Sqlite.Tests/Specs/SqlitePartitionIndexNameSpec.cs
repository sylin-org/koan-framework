using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Lexical;
using Koan.Data.Core.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0160: SQLite index names are database-wide, so a declared <c>[Index(Name = ...)]</c> is used as written on the
/// entity's own table and qualified by a hash of the table name on a partition table — the rule DATA-0146 gives
/// PostgreSQL. One declaration yields one index per container; two different entities that declare one name are still
/// refused on their own tables.
/// </summary>
public sealed class SqlitePartitionIndexNameSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    private const string Audit = "audit";

    [Storage(Name = "KOAN_PARTITION_INDEX_NOTE")]
    public sealed class Note : Entity<Note>
    {
        [Index(Name = "ix_partition_note_topic")] public string Topic { get; set; } = "";
    }

    [Storage(Name = "KOAN_PARTITION_INDEX_ARTICLE")]
    public sealed class Article : Entity<Article>
    {
        [Index(Name = "ix_partition_article_topics")] public string Topics { get; set; } = "";
        [Index(Name = "ix_partition_article_author")] public string Author { get; set; } = "";
        [Index(Name = "ix_partition_article_state_at", Order = 0)] public string State { get; set; } = "";
        [Index(Name = "ix_partition_article_state_at", Order = 1)] public DateTimeOffset At { get; set; }
        [Index] public int Revision { get; set; }
        public string Title { get; set; } = "";
    }

    [Storage(Name = "KOAN_PARTITION_INDEX_FIRST_OWNER")]
    public sealed class FirstOwner : Entity<FirstOwner>
    {
        [Index(Name = "ix_partition_shared_code")] public string Code { get; set; } = "";
    }

    [Storage(Name = "KOAN_PARTITION_INDEX_SECOND_OWNER")]
    public sealed class SecondOwner : Entity<SecondOwner>
    {
        [Index(Name = "ix_partition_shared_code")] public string Code { get; set; } = "";
    }

    [Storage(Name = "KOAN_PARTITION_INDEX_LEGACY")]
    public sealed class Legacy : Entity<Legacy>
    {
        [Index(Name = "ix_partition_legacy_code")] public string Code { get; set; } = "";
    }

    [Storage(Name = "KOAN_PARTITION_INDEX_MEMO")]
    public sealed class Memo : Entity<Memo>
    {
        [Index(Name = "ix_partition_memo_topic")] public string Topic { get; set; } = "";
        [Lexical] public string Title { get; set; } = "";
    }

    [Fact]
    public async Task A_partition_copy_of_an_entity_with_a_declared_index_saves()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        await new Note { Id = "a", Topic = "t" }.Save();
        await Data<Note, string>.Upsert(new Note { Id = "a#v1", Topic = "t" }, Audit);

        (await Note.Get("a"))!.Topic.Should().Be("t");
        (await Data<Note, string>.Get("a#v1", Audit))!.Topic.Should().Be("t");
        (await Note.Get("a#v1")).Should().BeNull("the copy lives in the partition table, not the entity's own");
    }

    [Fact]
    public async Task A_partition_copy_with_several_named_indexes_commits_atomically_with_the_change_it_records()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var article = await new Article
        {
            Id = "article-1", Topics = "lanterns", Author = "ines", State = "open", At = DateTimeOffset.UtcNow, Title = "first"
        }.Save();

        using (EntityContext.Transaction("edit", TransactionGuarantee.Atomic))
        {
            await Data<Article, string>.Upsert(new Article
            {
                Id = "article-1#r1", Topics = article.Topics, Author = article.Author, State = article.State,
                At = article.At, Revision = article.Revision, Title = article.Title
            }, Audit);
            article.Title = "second";
            article.Revision = 1;
            await article.Save();
            await EntityContext.Commit();
        }

        (await Article.Get("article-1"))!.Title.Should().Be("second");
        (await Data<Article, string>.Get("article-1#r1", Audit))!.Title.Should().Be("first");
    }

    [Fact]
    public async Task The_entity_table_keeps_the_declared_names_and_each_partition_table_holds_its_own_qualified_copies()
    {
        RequireBackingStore();
        const string root = "KOAN_PARTITION_INDEX_ARTICLE";
        const string archive = "archive";
        await using (var host = await BootAsync())
        {
            await new Article { Id = "names-1", Topics = "kettles", Author = "omar", State = "open", Title = "root" }.Save();
            await Data<Article, string>.Upsert(new Article { Id = "names-1#r1", Topics = "kettles", Title = "audit" }, Audit);
            await Data<Article, string>.Upsert(new Article { Id = "names-1#r2", Topics = "kettles", Title = "archive" }, archive);
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder(Fixture.ConnectionString)
        {
            Pooling = false
        }.ToString());
        await connection.OpenAsync();

        (await Indexes(connection, root)).Should().BeEquivalentTo(
            "ix_partition_article_topics",
            "ix_partition_article_author",
            "ix_partition_article_state_at",
            $"IX_{root}_Revision");
        foreach (var partition in new[] { Audit, archive })
        {
            var table = $"{root}#{partition}";
            (await Indexes(connection, table)).Should().BeEquivalentTo(
                $"ix_partition_article_topics_{TableHash(table)}",
                $"ix_partition_article_author_{TableHash(table)}",
                $"ix_partition_article_state_at_{TableHash(table)}",
                $"IX_{table}_Revision");
        }
        TableHash($"{root}#{Audit}").Should().NotBe(TableHash($"{root}#{archive}"));

        // The copy is an index the planner uses for the partition's own reads, as the declared one is for the entity's.
        await using var explain = connection.CreateCommand();
        explain.CommandText = $"""
            EXPLAIN QUERY PLAN
            SELECT "Id" FROM "{root}#{Audit}" AS koan_row
            WHERE json_extract(koan_row."Json", '$."Topics"') = 'kettles'
            """;
        var details = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
            while (await reader.ReadAsync()) details.Add(reader.GetString(3));
        details.Should().Contain(detail => detail.Contains(
            $"USING INDEX ix_partition_article_topics_{TableHash($"{root}#{Audit}")}", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class WarningLog : ILoggerProvider, ILogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Warnings { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Enqueue(formatter(state, exception));
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task Two_entities_that_declare_one_name_are_still_refused_on_their_own_tables()
    {
        RequireBackingStore();
        var logs = new WarningLog();
        await using var host = await BootAsync(services => services.AddSingleton<ILoggerProvider>(logs));
        // The boot settles it in one order and says so, whichever the host goes on to use.
        logs.Warnings.Should().Contain(warning => warning.Contains("SecondOwner: Index 'ix_partition_shared_code'", StringComparison.Ordinal));
        await new FirstOwner { Id = "first", Code = "c-1" }.Save();

        var second = () => new SecondOwner { Id = "second", Code = "c-1" }.Save();

        var refusal = (await second.Should().ThrowAsync<Exception>()).Which;
        Messages(refusal).Should().Contain(message =>
            message.Contains("Index 'ix_partition_shared_code'", StringComparison.Ordinal) &&
            message.Contains("already owned by SQLite table 'KOAN_PARTITION_INDEX_FIRST_OWNER'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_partition_table_holding_the_declared_name_itself_is_refused_with_the_statement_that_drops_it()
    {
        RequireBackingStore();
        const string root = "KOAN_PARTITION_INDEX_LEGACY";
        var direct = new SqliteConnectionStringBuilder(Fixture.ConnectionString) { Pooling = false }.ToString();
        await using (var host = await BootAsync())
            await Data<Legacy, string>.Upsert(new Legacy { Id = "legacy-1", Code = "c-1" }, Audit);

        // A database whose partition table took the declared name for itself, as one provisioned before each partition
        // table had a copy of its own would hold it.
        await using (var connection = new SqliteConnection(direct))
        {
            await connection.OpenAsync();
            await using var claim = connection.CreateCommand();
            // The entity's own table, made at start, holds the name until this takes it.
            claim.CommandText = $"""DROP INDEX "ix_partition_legacy_code"; CREATE INDEX "ix_partition_legacy_code" ON "{root}#{Audit}" (json_extract("Json", '$."Code"'))""";
            await claim.ExecuteNonQueryAsync();
        }

        await using (var host = await BootAsync())
        {
            var save = () => new Legacy { Id = "legacy-2", Code = "c-2" }.Save();
            Messages((await save.Should().ThrowAsync<Exception>()).Which).Should().Contain(message =>
                message.Contains($"already owned by SQLite table '{root}#{Audit}'", StringComparison.Ordinal) &&
                message.Contains("""DROP INDEX "ix_partition_legacy_code";""", StringComparison.Ordinal));
        }

        await using (var connection = new SqliteConnection(direct))
        {
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = """DROP INDEX "ix_partition_legacy_code";""";
            await drop.ExecuteNonQueryAsync();
        }

        await using (var host = await BootAsync())
        {
            await new Legacy { Id = "legacy-2", Code = "c-2" }.Save();
            (await Legacy.Get("legacy-2"))!.Code.Should().Be("c-2");
        }
        await using (var connection = new SqliteConnection(direct))
        {
            await connection.OpenAsync();
            (await Indexes(connection, root)).Should().BeEquivalentTo("ix_partition_legacy_code");
            (await Indexes(connection, $"{root}#{Audit}")).Should().BeEquivalentTo(
                $"ix_partition_legacy_code_{TableHash($"{root}#{Audit}")}");
        }
    }

    [Fact]
    public async Task A_partition_of_an_entity_with_a_lexical_index_and_a_named_index_searches_its_own_rows()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var kept = await new Memo { Id = "memo-1", Topic = "root", Title = "copper lantern" }.Save();
        await Data<Memo, string>.Upsert(new Memo { Id = "memo-1#v1", Topic = "root", Title = "copper kettle" }, Audit);

        (await Memo.Lexical.Search("copper", s => s.Top(10))).Should().ContainSingle().Which.Id.Should().Be(kept.Id);
        using (EntityContext.Partition(Audit))
        {
            (await Memo.Lexical.Search("copper", s => s.Top(10))).Should().ContainSingle().Which.Id.Should().Be("memo-1#v1");
            (await Memo.Lexical.Search("lantern", s => s.Top(10))).Should().BeEmpty("the partition indexes only its own rows");
        }
    }

    /// <summary>The independent statement of the rule: eight lower-case hex characters of the SHA-256 of the table name.</summary>
    private static string TableHash(string table) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(table)), 0, 4).ToLowerInvariant();

    private static async Task<IReadOnlyList<string>> Indexes(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = @table AND name NOT LIKE 'sqlite_%'";
        command.Parameters.AddWithValue("@table", table);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static IEnumerable<string> Messages(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            yield return current.Message;
    }
}
