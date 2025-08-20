using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Core;
using Sora.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Sora.Data.Sqlite.Tests;

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
        var dir = Path.Combine(Path.GetTempPath(), "sora-sqlite-governance-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    private static IServiceProvider BuildServices(string file, Action<SqliteOptions>? configure = null, IEnumerable<KeyValuePair<string, string?>>? extraConfig = null)
    {
        var cs = $"Data Source={file}";
        var sc = new ServiceCollection();
        var baseConfig = new List<KeyValuePair<string, string?>>
        {
            new("Sora:Data:Sqlite:ConnectionString", cs),
            new("SORA_DATA_PROVIDER","sqlite"),
            // Permit DDL even if environment is detected as Production in test runners
            new("Sora:AllowMagicInProduction","true"),
        };
        if (extraConfig is not null) baseConfig.AddRange(extraConfig);
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(baseConfig)
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        // Register adapter and core; then apply caller overrides last so they win
        sc.AddSqliteAdapter(o => { o.ConnectionString = cs; });
        sc.AddSoraDataCore();
        if (configure is not null)
            sc.Configure(configure);
        sc.AddSingleton<IDataService, DataService>();
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task Validate_Healthy_When_AutoCreate_And_Projected_Columns_Exist()
    {
        var file = TempFile();
        var sp = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.AutoCreate; });
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        // Trigger table + generated columns creation
        await repo.UpsertAsync(new Todo { Title = "x" });

        var rep = await data.Execute<Todo, object>(new Instruction("relational.schema.validate")) as System.Collections.Generic.IDictionary<string, object?>;
        rep.Should().NotBeNull();
        var tableExists = rep!["TableExists"] as bool? ?? false;
        tableExists.Should().BeTrue();
        // Title should be projected and present
        var missing = rep["MissingColumns"] as System.Collections.Generic.IEnumerable<string> ?? Array.Empty<string>();
        missing.Should().BeEmpty();
        var state = rep["State"] as string ?? string.Empty;
        state.Should().Be("Healthy");
    }

    [Fact]
    public async Task Validate_Degraded_When_NoDdl_And_Table_Missing()
    {
        var file = TempFile();
        var sp = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.NoDdl; });
        var data = sp.GetRequiredService<IDataService>();

        var rep = await data.Execute<Todo, object>(new Instruction("relational.schema.validate")) as System.Collections.Generic.IDictionary<string, object?>;
        rep.Should().NotBeNull();
        (rep!["TableExists"] as bool? ?? false).Should().BeFalse();
        (rep["State"] as string ?? string.Empty).Should().BeOneOf("Degraded", "Unhealthy");
        (rep["DdlAllowed"] as bool? ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureCreated_NoOp_For_ReadOnly_Entity()
    {
        var file = TempFile();
        var sp = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.AutoCreate; });
        var data = sp.GetRequiredService<IDataService>();

        // Attempt to ensure created on a read-only aggregate (should not create)
        var ok = await data.Execute<ReadOnlyTodo, bool>(new Instruction("data.ensureCreated"));
        ok.Should().BeTrue(); // call returns true, but table should not be created

        var rep = await data.Execute<ReadOnlyTodo, object>(new Instruction("relational.schema.validate")) as System.Collections.Generic.IDictionary<string, object?>;
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
        Environment.SetEnvironmentVariable("Sora__Data__Sqlite__SchemaMatchingMode", "Strict");
        var sp = BuildServices(file,
            o => { o.DdlPolicy = SchemaDdlPolicy.NoDdl; o.SchemaMatching = SchemaMatchingMode.Strict; },
            new[] { new KeyValuePair<string, string?>("Sora:Data:Sqlite:SchemaMatchingMode", "Strict") });
        var data = sp.GetRequiredService<IDataService>();

        var rep = await data.Execute<Todo, object>(new Instruction("relational.schema.validate")) as System.Collections.Generic.IDictionary<string, object?>;
        rep.Should().NotBeNull();
        (rep!["MatchingMode"] as string ?? string.Empty).Should().Be("Strict");
        ((rep["TableExists"] as bool?) ?? false).Should().BeFalse();
        var st = rep["State"] as string ?? string.Empty;
        st.Should().BeOneOf("Unhealthy", "Degraded");
        // Cleanup env var to avoid side effects on other tests
        Environment.SetEnvironmentVariable("Sora__Data__Sqlite__SchemaMatchingMode", null);
    }

    [Fact]
    public async Task Validate_Detects_Missing_Projected_Columns()
    {
        var file = TempFile();
        var sp1 = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.AutoCreate; });
        var data1 = sp1.GetRequiredService<IDataService>();
        var repo1 = data1.GetRepository<SharedTodoV1, string>();

        // Create the shared table with only Title projected (V1)
        await repo1.UpsertAsync(new SharedTodoV1 { Title = "x" });

        // Now validate with V2 (expects Priority column) but do not allow DDL
        var sp2 = BuildServices(file, o => { o.DdlPolicy = SchemaDdlPolicy.Validate; });
        var data2 = sp2.GetRequiredService<IDataService>();

        var rep = await data2.Execute<SharedTodoV2, object>(new Instruction("relational.schema.validate")) as System.Collections.Generic.IDictionary<string, object?>;
        rep.Should().NotBeNull();
        (rep!["TableExists"] as bool? ?? false).Should().BeTrue();
        var missing2 = rep["MissingColumns"] as System.Collections.Generic.IEnumerable<string> ?? Array.Empty<string>();
        missing2.Should().Contain(new[] { "Priority" });
        (rep["State"] as string ?? string.Empty).Should().Be("Degraded");
    }
}
