using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// DATA-0162: a set past the statement's parameters binds as a table-valued parameter of a Koan set type, whose name
/// carries its shape. A source that may not create storage objects, or whose login is denied <c>CREATE TYPE</c>, never
/// creates one and never fails an operation for it, and its lists stay one parameter per member within the bound; a set
/// needing a missing type, or a type of another shape, is refused by name with the DDL that works; a type created after a
/// refusal binds without a restart; a type dropped later is created again where the source may create, or refused by
/// name where it may not; and a text set compares under the column's collation exactly as a lone parameter does.
/// </summary>
public sealed class SqlServerSetTypeRefusalSpec(SqlServerFixture fixture)
{
    [Fact(DisplayName = "SQL Server: a large set on a source that may not create types is refused with the DDL to run")]
    public async Task A_large_set_without_its_type_is_refused_with_the_ddl()
    {
        await Execute("""
            IF SCHEMA_ID(N'nosets') IS NULL EXEC(N'CREATE SCHEMA [nosets]');
            DROP TABLE IF EXISTS [nosets].[NOSET_ROWS];
            CREATE TABLE [nosets].[NOSET_ROWS] ([ID] bigint PRIMARY KEY, [NAME] nvarchar(200) NOT NULL);
            INSERT INTO [nosets].[NOSET_ROWS] VALUES (1, N'one'), (2, N'two');
            """);
        await using var host = await External("NoSets", "nosets", koan =>
            koan.Data.Source("NoSets").Map<NoSetRow>(map => map
                .Container("NOSET_ROWS").Key(row => row.Id).Name("ID").Property(row => row.Name).Name("NAME")));

        var small = new List<long> { 1, 2 };
        var large = Enumerable.Range(1, 2_502).Select(static i => (long)i).ToList();
        using (EntityContext.Source("NoSets"))
        {
            (await NoSetRow.Query(row => small.Contains(row.Id))).Should().HaveCount(2, "a set within the parameters needs no type");
            var refusal = (await FluentActions.Invoking(() => NoSetRow.Query(row => large.Contains(row.Id)))
                .Should().ThrowAsync<NotSupportedException>()).Which;
            refusal.Message.Should().Contain($"[nosets].[{TypeName("bigint", "bigint")}]")
                .And.Contain($"CREATE TYPE [nosets].[{TypeName("bigint", "bigint")}] AS TABLE");
        }
        (await Scalar("SELECT COUNT(*) FROM sys.types WHERE is_table_type = 1 AND schema_id = SCHEMA_ID(N'nosets')"))
            .Should().Be(0, "an External source creates no storage object");
    }

    [Fact(DisplayName = "SQL Server: a text set under a column collation that is not the database's answers what Eq answers")]
    public async Task A_text_set_yields_to_the_column_collation()
    {
        await using (var managed = await KoanIntegrationHost.Configure()
                         .WithSettings(fixture.SettingsForBoot())
                         .ConfigureServices(services => services.AddKoan())
                         .StartAsync(TestContext.Current.CancellationToken))
            await new CollationProbe { Name = "creates the set types in dbo" }.Save();

        await Execute("""
            DROP TABLE IF EXISTS [dbo].[CS_ROWS];
            CREATE TABLE [dbo].[CS_ROWS] ([ID] bigint PRIMARY KEY, [NAME] nvarchar(200) COLLATE Latin1_General_CS_AS NOT NULL);
            INSERT INTO [dbo].[CS_ROWS] VALUES (1, N'Alpha'), (2, N'alpha'), (3, N'beta');
            """);
        await using var host = await External("CaseSensitive", "dbo", koan =>
            koan.Data.Source("CaseSensitive").Map<CaseSensitiveRow>(map => map
                .Container("CS_ROWS").Key(row => row.Id).Name("ID").Property(row => row.Name).Name("NAME")));

        using (EntityContext.Source("CaseSensitive"))
        {
            var eq = (await CaseSensitiveRow.Query(row => row.Name == "alpha")).Select(static row => row.Id).ToArray();
            eq.Should().Equal(2L);
            foreach (var size in new[] { 2, 2_502 })
            {
                var names = new[] { "alpha" }.Concat(Enumerable.Range(0, size - 1).Select(static i => $"pad-{i}")).ToList();
                (await CaseSensitiveRow.Query(row => names.Contains(row.Name))).Select(static row => row.Id)
                    .Should().Equal(eq, $"an In of {size} answers what Eq answers under the column's collation");
            }
        }
    }

    [Fact(DisplayName = "SQL Server: a set type of another shape under Koan's name is refused, never used")]
    public async Task A_set_type_of_another_shape_is_refused()
    {
        var floatType = TypeName("float", "float");
        await Execute($"""
            IF SCHEMA_ID(N'shape') IS NULL EXEC(N'CREATE SCHEMA [shape]');
            DROP TABLE IF EXISTS [shape].[SHAPE_ROWS];
            IF TYPE_ID(N'[shape].[{floatType}]') IS NOT NULL DROP TYPE [shape].[{floatType}];
            IF TYPE_ID(N'[shape].[koan_set_float]') IS NOT NULL DROP TYPE [shape].[koan_set_float];
            CREATE TYPE [shape].[{floatType}] AS TABLE ([koan_member] real NOT NULL);
            CREATE TYPE [shape].[koan_set_float] AS TABLE ([koan_member] real NOT NULL);
            CREATE TABLE [shape].[SHAPE_ROWS] ([ID] bigint PRIMARY KEY, [RATIO] float NOT NULL);
            INSERT INTO [shape].[SHAPE_ROWS] VALUES (1, 0.1), (2, 2.5);
            """);
        await using var host = await External("Shape", "shape", koan =>
            koan.Data.Source("Shape").Map<ShapeRow>(map => map
                .Container("SHAPE_ROWS").Key(row => row.Id).Name("ID").Property(row => row.Ratio).Name("RATIO")));

        var ratios = new[] { 0.1 }.Concat(Enumerable.Range(0, 2_501).Select(static i => 1_000_000.5 + i)).ToList();
        using (EntityContext.Source("Shape"))
        {
            (await ShapeRow.Query(row => row.Ratio == 0.1)).Should().ContainSingle();
            var refusal = (await FluentActions.Invoking(() => ShapeRow.Query(row => ratios.Contains(row.Ratio)))
                .Should().ThrowAsync<NotSupportedException>()).Which;
            refusal.Message.Should().Contain($"[shape].[{floatType}] exists here as ([koan_member] real NOT NULL)")
                .And.Contain($"Replace it: DROP TYPE [shape].[{floatType}]; CREATE TYPE [shape].[{floatType}] AS TABLE " +
                             "([koan_member] float NOT NULL)");
        }
    }

    [Fact(DisplayName = "SQL Server: a set type of another shape, dropped as the refusal says, is created by a source that may create")]
    public async Task A_dropped_set_type_of_another_shape_is_created_again()
    {
        var floatType = TypeName("float", "float");
        await Execute($"""
            IF SCHEMA_ID(N'reshape') IS NULL EXEC(N'CREATE SCHEMA [reshape]');
            IF TYPE_ID(N'[reshape].[{floatType}]') IS NOT NULL DROP TYPE [reshape].[{floatType}];
            CREATE TYPE [reshape].[{floatType}] AS TABLE ([koan_member] real NOT NULL);
            """);
        await using var host = await Managed("Reshape", "reshape");

        var ratios = new[] { 0.5 }.Concat(Enumerable.Range(0, 2_501).Select(static i => 1_000_000.5 + i)).ToList();
        using (EntityContext.Source("Reshape"))
        {
            await new ReshapeRow { Id = "half", Ratio = 0.5 }.Save();
            var refusal = (await FluentActions.Invoking(() => ReshapeRow.Query(row => ratios.Contains(row.Ratio)))
                .Should().ThrowAsync<NotSupportedException>()).Which;
            refusal.Message.Should().Contain(
                $"Drop it (DROP TYPE [reshape].[{floatType}]) and Koan creates it at this source's next operation");
        }

        await Execute($"DROP TYPE [reshape].[{floatType}]");
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
        using (EntityContext.Source("Reshape"))
            (await ReshapeRow.Query(row => ratios.Contains(row.Ratio))).Select(static row => row.Id).Should().Equal("half");
        (await Scalar($"""
            SELECT TYPE_NAME(c.system_type_id) FROM sys.table_types tt JOIN sys.columns c ON c.object_id = tt.type_table_object_id
             WHERE tt.schema_id = SCHEMA_ID(N'reshape') AND tt.name = N'{floatType}'
            """)).Should().Be("float", "the source created the type in the shape its name carries");
    }

    [Fact(DisplayName = "SQL Server: a set type dropped after Koan created it is created again, and the operation answers")]
    public async Task A_set_type_dropped_later_is_created_again()
    {
        var bigintType = TypeName("bigint", "bigint");
        await Execute("IF SCHEMA_ID(N'regrow') IS NULL EXEC(N'CREATE SCHEMA [regrow]');");
        await using var host = await Managed("Regrow", "regrow");

        var counts = new List<long> { 7 }.Concat(Enumerable.Range(0, 2_501).Select(static i => 1_000_000L + i)).ToList();
        using (EntityContext.Source("Regrow"))
        {
            await new RegrowRow { Id = "seven", Tally = 7 }.Save();
            (await RegrowRow.Query(row => counts.Contains(row.Tally))).Select(static row => row.Id).Should().Equal("seven");
        }

        await Execute($"DROP TYPE [regrow].[{bigintType}]");
        using (EntityContext.Source("Regrow"))
        {
            (await RegrowRow.Query(row => counts.Contains(row.Tally))).Select(static row => row.Id)
                .Should().Equal(["seven"], "the server's missing type marks the catalog stale, Koan creates it, and the query runs once more");
        }
        (await Scalar($"SELECT COUNT(*) FROM sys.table_types WHERE schema_id = SCHEMA_ID(N'regrow') AND name = N'{bigintType}'"))
            .Should().Be(1);
    }

    [Fact(DisplayName = "SQL Server: a set type dropped under a source that may not create is refused by name at the next operation")]
    public async Task A_set_type_dropped_under_an_external_source_is_refused_by_name()
    {
        var bigintType = TypeName("bigint", "bigint");
        await Execute($"""
            IF SCHEMA_ID(N'gone') IS NULL EXEC(N'CREATE SCHEMA [gone]');
            DROP TABLE IF EXISTS [gone].[GONE_ROWS];
            IF TYPE_ID(N'[gone].[{bigintType}]') IS NOT NULL DROP TYPE [gone].[{bigintType}];
            CREATE TYPE [gone].[{bigintType}] AS TABLE ([koan_member] bigint NOT NULL);
            CREATE TABLE [gone].[GONE_ROWS] ([ID] bigint PRIMARY KEY, [NAME] nvarchar(200) NOT NULL);
            INSERT INTO [gone].[GONE_ROWS] VALUES (1, N'one');
            """);
        await using var host = await External("Gone", "gone", koan =>
            koan.Data.Source("Gone").Map<GoneRow>(map => map
                .Container("GONE_ROWS").Key(row => row.Id).Name("ID").Property(row => row.Name).Name("NAME")));

        var ids = Enumerable.Range(1, 2_502).Select(static i => (long)i).ToList();
        using (EntityContext.Source("Gone"))
            (await GoneRow.Query(row => ids.Contains(row.Id))).Select(static row => row.Id).Should().Equal(1L);

        await Execute($"DROP TYPE [gone].[{bigintType}]");
        using (EntityContext.Source("Gone"))
        {
            var refusal = (await FluentActions.Invoking(() => GoneRow.Query(row => ids.Contains(row.Id)))
                .Should().ThrowAsync<NotSupportedException>()).Which;
            refusal.Message.Should().Contain("may not create storage objects")
                .And.Contain($"CREATE TYPE [gone].[{bigintType}] AS TABLE ([koan_member] bigint NOT NULL)");
        }
    }

    [Fact(DisplayName = "SQL Server: a login denied CREATE TYPE saves and queries, refuses a large set by name, and binds a type created later")]
    public async Task A_login_denied_create_type_works_and_a_type_created_later_binds()
    {
        var password = $"K14-{Guid.CreateVersion7():N}aA1!";
        await Execute($"""
            IF SCHEMA_ID(N'denied') IS NULL EXEC(N'CREATE SCHEMA [denied]');
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'koan_denied') DROP USER [koan_denied];
            IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'koan_denied') DROP LOGIN [koan_denied];
            CREATE LOGIN [koan_denied] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;
            CREATE USER [koan_denied] FOR LOGIN [koan_denied] WITH DEFAULT_SCHEMA = [denied];
            GRANT CREATE TABLE TO [koan_denied];
            GRANT ALTER, SELECT, INSERT, UPDATE, DELETE, EXECUTE, REFERENCES ON SCHEMA::[denied] TO [koan_denied];
            """);
        var connection = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            UserID = "koan_denied",
            Password = password
        }.ConnectionString;
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:Denied:Adapter"] = "sqlserver",
                ["Koan:Data:Sources:Denied:ConnectionString"] = connection,
                ["Koan:Data:Sources:Denied:Schema"] = "denied",
                ["Koan:Data:Sources:Denied:StorageLifecycle"] = nameof(StorageLifecycle.Managed),
                ["Koan:Data:Sources:Denied:Access"] = nameof(DataSourceAccess.ReadWrite),
            })
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);

        var textType = TypeName("nvarchar", "nvarchar(4000)");
        var ids = new List<string> { "kept" }.Concat(Enumerable.Range(0, 2_501).Select(static i => $"pad-{i}")).ToList();
        using (EntityContext.Source("Denied"))
        {
            await new DeniedRow { Id = "kept", Name = "saved without set types", Tags = ["a", "b"] }.Save();
            (await DeniedRow.Get("kept"))!.Name.Should().Be("saved without set types");

            // Without set types, a list within the statement's bound binds a parameter per member, whatever its operator.
            (await DeniedRow.Query("""{"Tags":{"$in":["a","zz"]}}""")).Select(static row => row.Id).Should().Equal("kept");
            (await DeniedRow.Query("""{"Tags":{"$all":["a","b"]}}""")).Select(static row => row.Id).Should().Equal("kept");
            (await DeniedRow.Query("""{"Tags":{"$all":["a","zz"]}}""")).Should().BeEmpty();
            (await DeniedRow.Query("""{"Tags":{"$nin":["a","zz"]}}""")).Should().BeEmpty();
            (await DeniedRow.Query("""{"Tags":{"$nin":["y","zz"]}}""")).Select(static row => row.Id).Should().Equal("kept");

            var refusal = (await FluentActions.Invoking(() => DeniedRow.Query(row => ids.Contains(row.Id)))
                .Should().ThrowAsync<NotSupportedException>()).Which;
            refusal.Message.Should().Contain("login was denied creating it").And.Contain($"CREATE TYPE [denied].[{textType}] AS TABLE");
            var tags = JsonSerializer.Serialize(new Dictionary<string, object> { ["Tags"] = new Dictionary<string, object> { ["$in"] = ids } });
            (await FluentActions.Invoking(() => DeniedRow.Query(tags)).Should().ThrowAsync<NotSupportedException>())
                .Which.Message.Should().Contain($"CREATE TYPE [denied].[{textType}] AS TABLE");
        }
        (await Scalar("SELECT COUNT(*) FROM sys.types WHERE is_table_type = 1 AND schema_id = SCHEMA_ID(N'denied')"))
            .Should().Be(0, "the denied login created nothing");

        // The operator runs the DDL the refusal named; the same host binds the type once the catalog is read again.
        await Execute($"CREATE TYPE [denied].[{textType}] AS TABLE ([koan_member] nvarchar(4000) NOT NULL)");
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
        using (EntityContext.Source("Denied"))
        {
            _ = await DeniedRow.Get("kept");
            (await DeniedRow.Query(row => ids.Contains(row.Id))).Select(static row => row.Id).Should().Equal("kept");
        }
    }

    private Task<IntegrationHost> Managed(string source, string schema) =>
        KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
            {
                [$"Koan:Data:Sources:{source}:Adapter"] = "sqlserver",
                [$"Koan:Data:Sources:{source}:ConnectionString"] = fixture.ConnectionString,
                [$"Koan:Data:Sources:{source}:Schema"] = schema,
                [$"Koan:Data:Sources:{source}:StorageLifecycle"] = nameof(StorageLifecycle.Managed),
                [$"Koan:Data:Sources:{source}:Access"] = nameof(DataSourceAccess.ReadWrite),
            })
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);

    private Task<IntegrationHost> External(string source, string schema, Action<KoanApplicationBuilder> declare) =>
        KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
            {
                [$"Koan:Data:Sources:{source}:Adapter"] = "sqlserver",
                [$"Koan:Data:Sources:{source}:ConnectionString"] = fixture.ConnectionString,
                [$"Koan:Data:Sources:{source}:Schema"] = schema,
                [$"Koan:Data:Sources:{source}:StorageLifecycle"] = nameof(StorageLifecycle.External),
                [$"Koan:Data:Sources:{source}:Access"] = nameof(DataSourceAccess.ReadWrite),
            })
            .ConfigureServices(services => services.AddKoan(declare))
            .StartAsync(TestContext.Current.CancellationToken);

    // The name Koan gives a set type: its scalar, then the first eight hex digits of the SHA-256 of its column.
    private static string TypeName(string scalar, string sqlType) =>
        $"koan_set_{scalar}_" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"[koan_member] {sqlType} NOT NULL")))[..8];

    private async Task Execute(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    public sealed class NoSetRow : Entity<NoSetRow, long> { public string Name { get; set; } = ""; }
    public sealed class CaseSensitiveRow : Entity<CaseSensitiveRow, long> { public string Name { get; set; } = ""; }
    public sealed class ShapeRow : Entity<ShapeRow, long> { public double Ratio { get; set; } }
    public sealed class DeniedRow : Entity<DeniedRow>
    {
        public string Name { get; set; } = "";
        public List<string> Tags { get; set; } = [];
    }
    public sealed class ReshapeRow : Entity<ReshapeRow> { public double Ratio { get; set; } }
    public sealed class RegrowRow : Entity<RegrowRow> { public long Tally { get; set; } }
    public sealed class GoneRow : Entity<GoneRow, long> { public string Name { get; set; } = ""; }
    public sealed class CollationProbe : Entity<CollationProbe> { public string Name { get; set; } = ""; }
}
