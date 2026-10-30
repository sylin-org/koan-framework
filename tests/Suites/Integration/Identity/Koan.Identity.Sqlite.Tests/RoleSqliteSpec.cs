using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Transactions;
using Koan.Identity.Roles;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Identity.Sqlite.Tests;

public sealed class RoleSqliteSpec
{
    [Fact] public async Task Sqlite_round_trips_collection_membership_and_compiles_a_bounded_bag()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-role-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("group:sqlite", "SQLite group", ["global:topic_read"], new Dictionary<string, string> { ["color"] = "green" });
            await roles.Add("group:sqlite", "person:sqlite");
            var bag = await roles.Bag("person:sqlite", true);
            bag.Tokens.Should().Contain(["group:sqlite", "global:topic_read"]);
            (await roles.Get("group:sqlite"))!.Metadata["color"].Should().Be("green");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact] public async Task A_role_change_inside_an_atomic_scope_refuses_before_any_write_and_after_commit_the_bag_is_correct()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-role-tx-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:tx", "Transactional", ["global:tx"]);
            var outside = await roles.Bag("person:tx", true);

            using (EntityContext.Transaction("role-change", TransactionGuarantee.Atomic))
            {
                var add = () => roles.Add("role:tx", "person:tx");
                (await add.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("EntityContext.Commit");
                var overwrite = () => roles.Overwrite("tangent:tx", OverwriteSubjectKind.Role, "role:tx", [], ["global:tx"]);
                await overwrite.Should().ThrowAsync<NotSupportedException>();
                await EntityContext.Commit();
            }

            (await roles.Members("role:tx")).Items.Should().BeEmpty("the refused change wrote nothing");
            (await roles.Bag("person:tx", true)).Should().BeSameAs(outside, "nothing was invalidated by a refused change");
            await roles.Add("role:tx", "person:tx");
            (await roles.Bag("person:tx", true)).Tokens.Should().Contain(["role:tx", "global:tx"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact] public async Task Sqlite_round_trips_an_overwrite_and_its_resolution()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-overwrite-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define("role:guest", "Guest", ["global:view", "global:post"]);
            await roles.Add("role:guest", "person:guest");
            var until = DateTimeOffset.UtcNow.AddHours(1);
            await roles.Overwrite("tangent:workshop", OverwriteSubjectKind.Role, "role:guest", [], ["global:post"], until);

            var stored = (await roles.Overwrites("tangent:workshop")).Items.Should().ContainSingle().Subject;
            stored.SubjectKind.Should().Be(OverwriteSubjectKind.Role);
            stored.Deny.Should().Equal("global:post");
            stored.ExpiresAt.Should().BeCloseTo(until, TimeSpan.FromSeconds(1));

            var bag = await roles.Bag("person:guest", true);
            var here = bag.On("tangent:workshop", await roles.OverwritesFor(bag, "tangent:workshop"), DateTimeOffset.UtcNow);
            here.Holds("global:view").Should().BeTrue();
            here.Holds("global:post").Should().BeFalse();
            here.Explain("global:post").ContributingFacts.Should().ContainSingle().Which.Subject.Should().Be("role:guest");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// A role holds any number of members: each join writes its own row and leaves the role's document as it was,
    /// every member's bag compiles and evaluates, and the members read back a page at a time, each once.
    /// </summary>
    [Fact] public async Task Five_thousand_members_join_one_role_each_by_one_row_and_each_is_evaluated()
    {
        const int members = 5000;
        var path = Path.Combine(Path.GetTempPath(), $"koan-role-crowd-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path, fast: true);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            const string key = "role:crowd";
            const string resource = "tangent:crowd";
            var role = await roles.Define(key, "Crowd", ["global:post"]);
            await roles.Overwrite(resource, OverwriteSubjectKind.Role, key, ["global:pin"], []);

            for (var i = 0; i < members; i++)
                (await roles.Add(key, Person(i))).Id.Should().Be(RoleMember.KeyFor(key, Person(i)));

            (await roles.Get(key))!.UpdatedAt.Should().Be(role.UpdatedAt, "a join writes its membership row, never the role");
            (await RoleMember.Count.Where(row => row.RoleKey == key, CountStrategy.Exact)).Should().Be(members);

            for (var i = 0; i < members; i++)
            {
                var bag = await roles.Bag(Person(i), true);
                bag.InRole(key).Should().BeTrue();
                var here = bag.On(resource, await roles.OverwritesFor(bag, resource), DateTimeOffset.UtcNow);
                here.Holds("global:post").Should().BeTrue();
                here.Holds("global:pin").Should().BeTrue();
            }

            var read = new List<string>();
            for (string? after = null; ;)
            {
                var page = await roles.Members(key, after, take: 100);
                read.AddRange(page.Items.Select(member => member.PersonId));
                if ((after = page.Next) is null) break;
            }

            read.Should().Equal(Enumerable.Range(0, members).Select(Person), "each member once, in person order");
        }
        finally { Delete(path); }

        static string Person(int i) => $"person:crowd:{i:D5}";
    }

    /// <summary>
    /// SQLite plans the person read and the role-then-person page with the indexes Koan declares for them. The
    /// connector offers no hook that hands back the command text it issues, so the plans explained here are of SQL
    /// written in the shape the connector issues — its document reads, the scope, the cursor's seek, the order with its
    /// id tie-break, and the page — not of captured commands.
    /// </summary>
    [Fact] public async Task Reads_in_the_connectors_shape_by_person_and_by_role_then_person_are_planned_with_their_indexes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-role-index-{Guid.CreateVersion7():n}.db");
        try
        {
            await using (var host = await Start(path))
            {
                using var app = AppHost.PushScope(host.Services);
                var roles = host.Services.GetRequiredService<RoleCollection>();
                foreach (var key in new[] { "role:a", "role:b", "role:c" }) await roles.Define(key, key, ["global:read"]);
                await roles.Add("role:a", "person:holder");
                await roles.Add("role:c", "person:holder");
                for (var i = 0; i < 30; i++) await roles.Add("role:b", $"person:other:{i:D2}");

                (await roles.ForPerson("person:holder")).Select(role => role.Key).Should().Equal("role:a", "role:c");
                (await roles.Members("role:b", RoleReads.Cursor("person:other:27", RoleMember.KeyFor("role:b", "person:other:27")))).Items.Select(member => member.PersonId)
                    .Should().Equal("person:other:28", "person:other:29");
            }

            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            var table = (string?)await Scalar(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE '%RoleMember%'");
            table.Should().NotBeNull("the membership rows have a table");
            var indexes = await Indexes(connection, table!);
            var byPerson = indexes.Single(index => index.Sql.Contains("PersonId", StringComparison.Ordinal) && !index.Sql.Contains("RoleKey", StringComparison.Ordinal)).Name;
            var byRole = indexes.Single(index => index.Sql.Contains("RoleKey", StringComparison.Ordinal) && index.Sql.Contains("PersonId", StringComparison.Ordinal)).Name;

            (await Plan(connection, $"""
                SELECT "Id" FROM "{table}" AS koan_row
                WHERE json_extract(koan_row."Json", '$."PersonId"') = @p0
                ORDER BY json_extract(koan_row."Json", '$."RoleKey"') ASC, koan_row."Id" ASC
                LIMIT 101 OFFSET 0
                """, "person:holder")).Should().Contain(detail => detail.Contains(byPerson, StringComparison.Ordinal),
                "a person's memberships are planned with the person index");
            (await Plan(connection, $"""
                SELECT "Id" FROM "{table}" AS koan_row
                WHERE json_extract(koan_row."Json", '$."RoleKey"') = @p0
                  AND (json_extract(koan_row."Json", '$."PersonId"') > @p1
                       OR (json_extract(koan_row."Json", '$."PersonId"') = @p2 AND koan_row."Id" > @p3))
                ORDER BY json_extract(koan_row."Json", '$."PersonId"') ASC, koan_row."Id" ASC
                LIMIT 3 OFFSET 0
                """, "role:b", "person:other:27", "person:other:27", RoleMember.KeyFor("role:b", "person:other:27")))
                .Should().Contain(detail => detail.Contains(byRole, StringComparison.Ordinal),
                    "a page of a role's members after a cursor is planned with the role-then-person index");
        }
        finally { Delete(path); }
    }

    /// <summary>
    /// Nothing bounds how many overwrites an application holds: two thousand resources each with an everyone
    /// overwrite evaluate as Discord orders them through caches far smaller than the set, and one resource holding
    /// more people's overwrites than a page evaluates each person by their own row.
    /// </summary>
    [Fact] public async Task Two_thousand_resources_each_with_an_everyone_overwrite_evaluate_correctly_and_no_subject_is_bounded()
    {
        const int resources = 2000;
        var path = Path.Combine(Path.GetTempPath(), $"koan-overwrite-many-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path, fast: true, new()
            {
                ["Koan:Identity:Roles:MaxCachedResources"] = "64",
                ["Koan:Identity:Roles:MaxCachedPersonOverwrites"] = "64",
            });
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            host.Services.GetRequiredService<IOptions<RoleOptions>>().Value.MaxCachedResources.Should().Be(64);
            await roles.Define("role:reader", "Reader", ["global:view", "global:post"]);
            await roles.Add("role:reader", "person:reader");
            for (var i = 0; i < resources; i++)
                await roles.Overwrite(Resource(i), OverwriteSubjectKind.Role, RoleTokens.Everyone, i % 2 == 0 ? [] : ["global:pin"], i % 2 == 0 ? ["global:post"] : []);

            var bag = await roles.Bag("person:reader", true);
            var visitor = await roles.Bag(null, false);
            for (var i = 0; i < resources; i++)
            {
                var here = bag.On(Resource(i), await roles.OverwritesFor(bag, Resource(i)), DateTimeOffset.UtcNow);
                here.Holds("global:view").Should().BeTrue();
                here.Holds("global:post").Should().Be(i % 2 != 0, "the everyone overwrite denies it on every other resource");
                here.Holds("global:pin").Should().Be(i % 2 != 0);
                if (i % 2 == 0) here.Explain("global:post").ContributingFacts.Single().Subject.Should().Be(RoleTokens.Everyone);
                visitor.On(Resource(i), await roles.OverwritesFor(visitor, Resource(i)), DateTimeOffset.UtcNow).Holds("global:pin").Should().Be(i % 2 != 0);
            }

            var everyone = new List<string>();
            for (string? after = null; ;)
            {
                var page = await roles.OverwritesOf(OverwriteSubjectKind.Role, RoleTokens.Everyone, after, take: 100);
                everyone.AddRange(page.Items.Select(row => row.ResourceId));
                if ((after = page.Next) is null) break;
            }

            everyone.Should().Equal(Enumerable.Range(0, resources).Select(Resource), "everyone holds one overwrite per resource, read a page at a time");

            const string crowded = "tangent:crowded";
            for (var i = 0; i < 300; i++) await roles.Overwrite(crowded, OverwriteSubjectKind.Person, Visitor(i), [], ["global:view"]);
            var denied = new RoleBag(Visitor(150), true, ["role:reader"], ["global:view"]);
            var rows = await roles.OverwritesFor(denied, crowded);
            rows.Should().ContainSingle("a person's own overwrite is read by key, never with everyone else's").Which.SubjectId.Should().Be(Visitor(150));
            denied.On(crowded, rows, DateTimeOffset.UtcNow).Holds("global:view").Should().BeFalse();
            var stranger = new RoleBag("person:stranger", true, ["role:reader"], ["global:view"]);
            stranger.On(crowded, await roles.OverwritesFor(stranger, crowded), DateTimeOffset.UtcNow).Holds("global:view").Should().BeTrue();
        }
        finally { Delete(path); }

        static string Resource(int i) => $"tangent:{i:D4}";
        static string Visitor(int i) => $"person:crowded:{i:D3}";
    }

    /// <summary>SEC-0031 on a real store: positions past 9 order, shift and page as numbers, never as text.</summary>
    [Fact] public async Task Sqlite_orders_moves_and_closes_role_positions_as_numbers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-role-order-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await Start(path);
            using var app = AppHost.PushScope(host.Services);
            var roles = host.Services.GetRequiredService<RoleCollection>();
            await roles.Define(RoleTokens.Everyone, "Everyone");
            for (var i = 1; i <= 12; i++) await roles.Define(Key(i), $"Role {i}");
            (await Order(roles)).Should().Equal([.. Enumerable.Range(1, 12).Reverse().Select(Key), RoleTokens.Everyone]);

            (await roles.Move(Key(2), 11)).Position.Should().Be(11);
            (await roles.Define("role:sqlite:new", "New", position: 10)).Position.Should().Be(10);
            (await roles.Delete(Key(5))).Should().BeTrue();

            var order = await Order(roles);
            order.Should().Equal(Key(12), Key(2), Key(11), "role:sqlite:new", Key(10), Key(9), Key(8), Key(7), Key(6), Key(4), Key(3), Key(1), RoleTokens.Everyone);
            var positions = new List<int>();
            foreach (var key in order) positions.Add((await roles.Get(key))!.Position);
            positions.Should().Equal(Enumerable.Range(0, 13).Reverse(), "the order is dense, everyone at 0");
            (await roles.Page(page: 2, pageSize: 5)).Items.Select(role => role.Key).Should().Equal(order.Skip(5).Take(5), "a page is the order's next part");
        }
        finally { Delete(path); }

        static string Key(int i) => $"role:sqlite:{i:D2}";

        static async Task<IReadOnlyList<string>> Order(RoleCollection roles)
            => (await roles.Page(pageSize: 50)).Items.Select(role => role.Key).ToArray();
    }

    internal static async Task<object?> Scalar(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    internal static async Task<IReadOnlyList<(string Name, string Sql)>> Indexes(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND tbl_name = @table AND sql IS NOT NULL";
        command.Parameters.AddWithValue("@table", table);
        var indexes = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) indexes.Add((reader.GetString(0), reader.GetString(1)));
        return indexes;
    }

    internal static async Task<IReadOnlyList<string>> Plan(SqliteConnection connection, string sql, params string[] values)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue($"@p{i}", values[i]);
        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) details.Add(reader.GetString(3));
        return details;
    }

    internal static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>A host on a SQLite file. <paramref name="fast"/> pools connections and syncs at checkpoints rather than
    /// every commit, for the specs that write thousands of rows; <see cref="Delete"/> clears the pool first.</summary>
    internal static Task<IntegrationHost> Start(string path, bool fast = false, Dictionary<string, string?>? settings = null)
    {
        var all = new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling={fast}",
        };
        if (fast) all["Koan:Data:Sqlite:Synchronous"] = nameof(Koan.Data.Connector.Sqlite.SqliteSynchronous.Normal);
        foreach (var (key, value) in settings ?? []) all[key] = value;
        return KoanIntegrationHost.Configure().WithSettings(all).ConfigureServices(services => services.AddKoan()).StartAsync();
    }
}
