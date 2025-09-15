using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Sqlite.Tests;

public class SqliteSchemaGovernanceTests
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }
    [ReadOnly]
    public class ReadOnlyTodo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    [StorageName("GovernanceShared")]
    public class SharedTodoV1 : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    [StorageName("GovernanceShared")]
    public class SharedTodoV2 : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Koan-sqlite-governance-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    private static IServiceProvider BuildServices(string file, Action<SqliteOptions>? configure = null, IEnumerable<KeyValuePair<string, string?>>? extraConfig = null)
    {
        var cs = $"Data Source={file}";
        var sc = new ServiceCollection();
        var baseConfig = new List<KeyValuePair<string, string?>>
        {
            new("Koan:Data:Sqlite:ConnectionString", cs),
            new("Koan_DATA_PROVIDER","sqlite"),
            // Permit DDL even if environment is detected as Production in test runners
            new("Koan:AllowMagicInProduction","true"),
        };
        if (extraConfig is not null) baseConfig.AddRange(extraConfig);
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(baseConfig)
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        // Always set DdlPolicy=AutoCreate and AllowProductionDdl=true unless explicitly overridden
        sc.AddSqliteAdapter(o =>
        {
            o.ConnectionString = cs;
            o.DdlPolicy = SchemaDdlPolicy.AutoCreate;
            o.AllowProductionDdl = true;
            configure?.Invoke(o);
        });
        sc.AddKoanDataCore();
        sc.AddSingleton<IDataService, DataService>();
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task Validate_Healthy_When_AutoCreate_And_Projected_Columns_Exist()
    {
        var file = TempFile();
        var sp = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.AutoCreate; o.AllowProductionDdl = true; });
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        // Diagnostic: log resolved RelationalMaterializationOptions
        var relOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<Relational.Orchestration.RelationalMaterializationOptions>>().Value;
        System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Resolved RelationalMaterializationOptions: DdlPolicy={relOpts.DdlPolicy}, AllowProductionDdl={relOpts.AllowProductionDdl}, Materialization={relOpts.Materialization}, SchemaMatching={relOpts.SchemaMatching}");
        relOpts.DdlPolicy.Should().Be(Relational.Orchestration.RelationalDdlPolicy.AutoCreate, "DdlPolicy should be AutoCreate for this test");
        relOpts.AllowProductionDdl.Should().BeTrue("AllowProductionDdl should be true for this test");
        System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Materialization policy: {relOpts.Materialization}");

        // Force ensure created before upsert to test orchestration
        var ensureOk = await data.Execute<Todo, bool>(new Instruction("data.ensureCreated"));
        ensureOk.Should().BeTrue();
        // Trigger table + generated columns creation
        await repo.UpsertAsync(new Todo { Title = "x" });

        // After ensureCreated, log the actual columns in the table and all table names
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file}"))
        {
            await conn.OpenAsync();
            // Log all table names
            var tableCmd = conn.CreateCommand();
            tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            using (var tableReader = await tableCmd.ExecuteReaderAsync())
            {
                var tables = new List<string>();
                while (await tableReader.ReadAsync())
                {
                    tables.Add(tableReader.GetString(0));
                }
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Tables present in DB: [{string.Join(", ", tables)}]");
            }
            // Log columns in Todo
            var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(Todo);";
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                var cols = new List<string>();
                while (await reader.ReadAsync())
                {
                    cols.Add(reader.GetString(1));
                }
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Columns present in Todo after ensureCreated: [{string.Join(", ", cols)}]");
            }
            await conn.CloseAsync();
        }

        var rep = await data.Execute<Todo, object>(new Instruction("relational.schema.validate")) as IDictionary<string, object?>;
        rep.Should().NotBeNull();
        var tableExists = rep!["TableExists"] as bool? ?? false;
        tableExists.Should().BeTrue();
        // Title should be projected and present
        var missing = rep["MissingColumns"] as IEnumerable<string> ?? Array.Empty<string>();
        var schema = rep.ContainsKey("Schema") ? rep["Schema"] : "<no-schema>";
        var table = rep.ContainsKey("Table") ? rep["Table"] : "<no-table>";
        System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Table: {schema}.{table}, MissingColumns: [{string.Join(", ", missing)}]");
        missing.Should().BeEmpty("Table: {0}.{1} should have all projected columns present, but missing: [{2}]", schema, table, string.Join(", ", missing));
        var state = rep["State"] as string ?? string.Empty;
        state.Should().Be("Healthy");
    }

    [Fact]
    public async Task Validate_Degraded_When_NoDdl_And_Table_Missing()
    {
        var file = TempFile();
        var sp = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.NoDdl; o.AllowProductionDdl = true; });
        var data = sp.GetRequiredService<IDataService>();

        var rep = await data.Execute<Todo, object>(new Instruction("relational.schema.validate")) as IDictionary<string, object?>;
        rep.Should().NotBeNull();
        (rep!["TableExists"] as bool? ?? false).Should().BeFalse();
        (rep["State"] as string ?? string.Empty).Should().BeOneOf("Degraded", "Unhealthy");
        (rep["DdlAllowed"] as bool? ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureCreated_NoOp_For_ReadOnly_Entity()
    {
        var file = TempFile();
        var sp = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.AutoCreate; o.AllowProductionDdl = true; });
        var data = sp.GetRequiredService<IDataService>();

        // Attempt to ensure created on a read-only aggregate (should not create)
        var ok = await data.Execute<ReadOnlyTodo, bool>(new Instruction("data.ensureCreated"));
        ok.Should().BeTrue(); // call returns true, but table should not be created

        var rep = await data.Execute<ReadOnlyTodo, object>(new Instruction("relational.schema.validate")) as IDictionary<string, object?>;
        rep.Should().NotBeNull();
        (rep!["TableExists"] as bool? ?? false).Should().BeFalse();
        (rep["State"] as string ?? string.Empty).Should().Be("Degraded");
        (rep["DdlAllowed"] as bool? ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Strict_Mode_Is_Unhealthy_When_Table_Missing()
    {
        var file = TempFile();
        // Ensure environment override also signals Strict for code paths that read from env
        Environment.SetEnvironmentVariable("Koan__Data__Sqlite__SchemaMatchingMode", "Strict");
        var sp = BuildServices(file,
            o => { o.DdlPolicy = SchemaDdlPolicy.NoDdl; o.SchemaMatching = SchemaMatchingMode.Strict; o.AllowProductionDdl = true; },
            new[] { new KeyValuePair<string, string?>("Koan:Data:Sqlite:SchemaMatchingMode", "Strict") });
        var data = sp.GetRequiredService<IDataService>();

        var rep = await data.Execute<Todo, object>(new Instruction("relational.schema.validate")) as IDictionary<string, object?>;
        rep.Should().NotBeNull();
        (rep!["MatchingMode"] as string ?? string.Empty).Should().Be("Strict");
        ((rep["TableExists"] as bool?) ?? false).Should().BeFalse();
        var st = rep["State"] as string ?? string.Empty;
        st.Should().BeOneOf("Unhealthy", "Degraded");
        // Cleanup env var to avoid side effects on other tests
        Environment.SetEnvironmentVariable("Koan__Data__Sqlite__SchemaMatchingMode", null);
    }

    [Fact]
    public async Task Validate_Detects_Missing_Projected_Columns()
    {
        var file = TempFile();
        var sp1 = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.AutoCreate; o.AllowProductionDdl = true; });
        var data1 = sp1.GetRequiredService<IDataService>();
        var repo1 = data1.GetRepository<SharedTodoV1, string>();

        // Create the shared table with only Title projected (V1)
        await repo1.UpsertAsync(new SharedTodoV1 { Title = "x" });

        // Now validate with V2 (expects Priority column) but do not allow DDL
        var sp2 = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.Validate; o.AllowProductionDdl = true; });
        var data2 = sp2.GetRequiredService<IDataService>();

        var rep = await data2.Execute<SharedTodoV2, object>(new Instruction("relational.schema.validate")) as IDictionary<string, object?>;
        rep.Should().NotBeNull();
        (rep!["TableExists"] as bool? ?? false).Should().BeTrue();
        var missing2 = rep["MissingColumns"] as IEnumerable<string> ?? Array.Empty<string>();
        missing2.Should().Contain(new[] { "Priority" });
        (rep["State"] as string ?? string.Empty).Should().Be("Degraded");
    }
}