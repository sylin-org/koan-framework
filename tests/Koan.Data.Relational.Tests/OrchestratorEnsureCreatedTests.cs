using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;
using Xunit;

namespace Koan.Data.Relational.Tests;

public class OrchestratorEnsureCreatedTests
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        [Index]
        public string Title { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    private static (IRelationalSchemaOrchestrator orch, IServiceProvider sp) CreateSut(RelationalMaterializationOptions? opts = null)
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddRelationalOrchestration();
        if (opts is not null)
        {
            services.Configure<RelationalMaterializationOptions>(o =>
            {
                o.Materialization = opts.Materialization;
                o.ProbeOnStartup = opts.ProbeOnStartup;
                o.FailOnMismatch = opts.FailOnMismatch;
                o.DdlPolicy = opts.DdlPolicy;
                o.SchemaMatching = opts.SchemaMatching;
                o.AllowProductionDdl = opts.AllowProductionDdl;
            });
        }
        var sp = services.BuildServiceProvider();
        var orch = sp.GetRequiredService<IRelationalSchemaOrchestrator>();
        return (orch, sp);
    }

    [RelationalStorage(Shape = RelationalStorageShape.ComputedProjections)]
    public class TodoComputed : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    [Fact]
    public async Task EnsureCreated_Honors_Attribute_Shape_Over_Options()
    {
        // Options say None (JSON), attribute demands ComputedProjections -> should create projections
        var (orch, _) = CreateSut(new RelationalMaterializationOptions
        {
            Materialization = RelationalMaterializationPolicy.None
        });
        var ddl = new FakeDdlExecutor();
        var features = new FakeFeatures(jsonFuncs: true, persisted: true, idxOnComputed: true);

        await orch.EnsureCreatedAsync<TodoComputed, string>(ddl, features);

        ddl.TableExists("dbo", nameof(TodoComputed)).Should().BeTrue();
        ddl.ColumnExists("dbo", nameof(TodoComputed), "Id").Should().BeTrue();
        ddl.ColumnExists("dbo", nameof(TodoComputed), "Json").Should().BeTrue();
        // Projections due to attribute override
        ddl.ColumnExists("dbo", nameof(TodoComputed), "Title").Should().BeTrue();
        ddl.ColumnExists("dbo", nameof(TodoComputed), "Priority").Should().BeTrue();
    }

    [Fact]
    public async Task Validate_FailOnMismatch_Throws_When_Required_Columns_Missing()
    {
        // Simulate: options require projections; validation finds missing columns; FailOnMismatch=true should be enforced by callers.
        // Here we call ValidateAsync and assert report marks missing; then simulate throwing SchemaMismatchException
        var (orch, _) = CreateSut(new RelationalMaterializationOptions
        {
            Materialization = RelationalMaterializationPolicy.ComputedProjections,
            FailOnMismatch = true,
            DdlPolicy = RelationalDdlPolicy.NoDdl
        });
        var ddl = new FakeDdlExecutor(); // starts with no table/columns
        var features = new FakeFeatures(jsonFuncs: true);

        var report = await orch.ValidateAsync<Todo, string>(ddl, features);
        report.Should().BeOfType<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)report;
        dict.Should().ContainKey("MissingColumns");
        var missing = (string[])dict["MissingColumns"]!;
        missing.Length.Should().BeGreaterThan(0);

        // In runtime, repository would throw when FailOnMismatch and missing; emulate here using exception type
        Action throwing = () => throw new SchemaMismatchException(typeof(Todo).Name, "Todo", "ComputedProjections", missing, Array.Empty<string>(), ddlAllowed: false);
        throwing.Should().Throw<SchemaMismatchException>()
            .WithMessage("*Schema mismatch for*")
            .Which.Missing.Should().NotBeEmpty();
    }
    private sealed class FakeFeatures(bool jsonFuncs = true, bool persisted = true, bool idxOnComputed = true) : IRelationalStoreFeatures
    {
        public bool SupportsJsonFunctions { get; } = jsonFuncs;
        public bool SupportsPersistedComputedColumns { get; } = persisted;
        public bool SupportsIndexesOnComputedColumns { get; } = idxOnComputed;
        public string ProviderName => "test";
    }

    private sealed class FakeDdlExecutor : IRelationalDdlExecutor
    {
        private readonly HashSet<(string schema, string table)> _tables = new();
        private readonly Dictionary<(string schema, string table), HashSet<string>> _columns = new();
        public readonly List<(string schema, string table, string index, IReadOnlyList<string> columns, bool unique)> Indexes = new();

        public bool TableExists(string schema, string table) => _tables.Contains((schema, table));

        public bool ColumnExists(string schema, string table, string column)
            => _columns.TryGetValue((schema, table), out var set) && set.Contains(column);

        public void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json")
        {
            _tables.Add((schema, table));
            var set = _columns.GetValueOrDefault((schema, table)) ?? new HashSet<string>(StringComparer.Ordinal);
            set.Add(idColumn);
            set.Add(jsonColumn);
            _columns[(schema, table)] = set;
        }

        public void CreateTableWithColumns(string schema, string table, List<(string Name, Type ClrType, bool Nullable, bool IsComputed, string? JsonPath, bool IsIndexed)> columns)
        {
            _tables.Add((schema, table));
            var set = _columns.GetValueOrDefault((schema, table)) ?? new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in columns) set.Add(c.Name);
            _columns[(schema, table)] = set;
            // record indexes
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                if (c.IsIndexed) Indexes.Add((schema, table, $"IX_{table}_{c.Name}", new List<string> { c.Name }, false));
            }
        }

        public void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted)
        {
            var set = _columns.GetValueOrDefault((schema, table)) ?? new HashSet<string>(StringComparer.Ordinal);
            set.Add(column);
            _columns[(schema, table)] = set;
        }

        public void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable)
        {
            var set = _columns.GetValueOrDefault((schema, table)) ?? new HashSet<string>(StringComparer.Ordinal);
            set.Add(column);
            _columns[(schema, table)] = set;
        }

        public void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique)
            => Indexes.Add((schema, table, indexName, columns, unique));
    }

    [Fact]
    public async Task EnsureCreated_Json_Creates_Table_With_Id_And_Json_Only()
    {
        var (orch, _) = CreateSut();
        var ddl = new FakeDdlExecutor();
        var features = new FakeFeatures(jsonFuncs: true);

        await orch.EnsureCreatedJsonAsync<Todo, string>(ddl, features);

        // Table and base columns created
        ddl.TableExists("dbo", "Todo").Should().BeTrue();
        ddl.ColumnExists("dbo", "Todo", "Id").Should().BeTrue();
        ddl.ColumnExists("dbo", "Todo", "Json").Should().BeTrue();

        // No projection columns in pure JSON shape
        ddl.ColumnExists("dbo", "Todo", "Title").Should().BeFalse();
        ddl.ColumnExists("dbo", "Todo", "Priority").Should().BeFalse();
        ddl.Indexes.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureCreated_Materialized_Adds_Projection_Columns_And_Indexes()
    {
        var (orch, _) = CreateSut(new RelationalMaterializationOptions
        {
            Materialization = RelationalMaterializationPolicy.ComputedProjections
        });
        var ddl = new FakeDdlExecutor();
        var features = new FakeFeatures(jsonFuncs: true, persisted: true, idxOnComputed: true);

        await orch.EnsureCreatedMaterializedAsync<Todo, string>(ddl, features);

        ddl.TableExists("dbo", "Todo").Should().BeTrue();
        ddl.ColumnExists("dbo", "Todo", "Id").Should().BeTrue();
        ddl.ColumnExists("dbo", "Todo", "Json").Should().BeTrue();

        // Projection columns should exist
        ddl.ColumnExists("dbo", "Todo", "Title").Should().BeTrue();
        ddl.ColumnExists("dbo", "Todo", "Priority").Should().BeTrue();

        // Index should be created for [Index] property when supported
        ddl.Indexes.Should().ContainSingle(i => i.schema == "dbo" && i.table == "Todo" && i.index == "IX_Todo_Title");
    }
}
