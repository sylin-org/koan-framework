using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Connector.Cockroach;
using Koan.Data.Core;
using Koan.Data.Core.Options;
using Koan.Data.Relational.Initialization;
using Koan.Data.Relational.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Relational.Tests;

public sealed class RelationalOwnershipSpec
{
    [Fact]
    public async Task Schema_and_resolved_table_remain_request_local()
    {
        using var provider = Host(source =>
        {
            source.Map<OrderA>(map => map.Container("orders_a").Key(order => order.Id).Name("ID"));
            source.Map<OrderB>(map => map.Container("orders_b").Key(order => order.Id).Name("ID"));
        });
        var orchestrator = new RelationalSchemaOrchestrator();
        var ddl = new ProbeDdl();

        var strict = await orchestrator.ValidateAsync(
            Mapping<OrderA>(provider),
            ddl,
            new Features("postgres"),
            new RelationalSchemaPolicy
            {
                DefaultSchema = "tenant_a",
                Matching = RelationalSchemaMatchingMode.Strict,
                Ddl = RelationalDdlPolicy.NoDdl
            });

        var relaxed = await orchestrator.ValidateAsync(
            Mapping<OrderB>(provider),
            ddl,
            new Features("sqlite"),
            new RelationalSchemaPolicy
            {
                DefaultSchema = "main",
                Matching = RelationalSchemaMatchingMode.Relaxed,
                Ddl = RelationalDdlPolicy.AutoCreate
            });

        strict.Plan.Table.Schema.Should().Be("tenant_a");
        strict.Plan.Table.Name.Should().Be("orders_a");
        relaxed.Plan.Table.Schema.Should().Be("main");
        relaxed.Plan.Table.Name.Should().Be("orders_b");

        // An absent table is not serviceable on any matching mode. Relaxed tolerates drift, not absence.
        strict.State.Should().Be("Unhealthy");
        relaxed.State.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Matching_mode_decides_whether_drift_stops_the_mapping()
    {
        using var provider = Host(source => source.Map<OrderA>(map => map
            .Container("orders_a")
            .Key(order => order.Id).Name("ID")
            .Property(order => order.Reference).Name("REF")));
        var orchestrator = new RelationalSchemaOrchestrator();
        var mapping = Mapping<OrderA>(provider);
        // The key is as declared; an ordinary scalar is not. Nothing about that stops a read or a write.
        var ddl = new ProbeDdl(new RelationalTableShape(
            new Dictionary<string, RelationalColumnState?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = new("ID", Nullable: false, ClrType: typeof(string)),
                ["REF"] = new("REF", Nullable: true, ClrType: typeof(int))
            },
            ["ID"]));

        var relaxed = await orchestrator.ValidateAsync(
            mapping, ddl, new Features("sqlite"), new RelationalSchemaPolicy { DefaultSchema = "main" });
        var strict = await orchestrator.ValidateAsync(
            mapping, ddl, new Features("sqlite"),
            new RelationalSchemaPolicy { DefaultSchema = "main", Matching = RelationalSchemaMatchingMode.Strict });

        relaxed.Findings.Should().ContainSingle(finding => finding.Subject == "REF");
        relaxed.IsComplete.Should().BeFalse("the difference is reported either way");
        relaxed.IsServiceable.Should().BeTrue("Relaxed matching tolerates drift outside identity");
        relaxed.State.Should().Be("Degraded");

        strict.IsServiceable.Should().BeFalse();
        strict.Corrective.Should().ContainSingle(finding => finding.Subject == "REF");
        strict.State.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Identity_drift_stops_the_mapping_on_every_matching_mode()
    {
        using var provider = Host(source => source.Map<OrderA>(map => map
            .Container("orders_a")
            .Key(order => order.Id).Name("ID")));
        var orchestrator = new RelationalSchemaOrchestrator();
        // The store addresses rows by a different column than the mapping does, so no read or write is safe.
        var ddl = new ProbeDdl(new RelationalTableShape(
            new Dictionary<string, RelationalColumnState?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = new("ID", Nullable: false, ClrType: typeof(string)),
                ["LEGACY_KEY"] = new("LEGACY_KEY", Nullable: false, ClrType: typeof(string))
            },
            ["LEGACY_KEY"]));

        var validation = await orchestrator.ValidateAsync(
            Mapping<OrderA>(provider), ddl, new Features("sqlite"),
            new RelationalSchemaPolicy { DefaultSchema = "main" });

        validation.IsServiceable.Should().BeFalse();
        validation.Corrective.Should().ContainSingle(finding => finding.Subject == "PrimaryKey");
    }

    [Fact]
    public async Task A_required_index_this_store_cannot_build_refuses_the_container()
    {
        using var provider = Host(source =>
        {
            source.Map<HotPath>(map => map.Container("hot")
                .Key(order => order.Id).Name("ID")
                .Property(order => order.Lookup).Name("Lookup"));
            source.Map<ColdPath>(map => map.Container("cold")
                .Key(order => order.Id).Name("ID")
                .Property(order => order.Lookup).Name("Lookup"));
        });
        var orchestrator = new RelationalSchemaOrchestrator();
        var features = new Features("sqlite");
        var policy = new RelationalSchemaPolicy { DefaultSchema = "main" };
        // This store answers SupportsMappedIndexes = false, so neither index can be built.
        var ddl = new ProbeDdl(new RelationalTableShape(
            new Dictionary<string, RelationalColumnState?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = null,
                ["Lookup"] = null
            },
            ["ID"]));

        var required = await orchestrator.ValidateAsync(Mapping<HotPath>(provider), ddl, features, policy);
        var optional = await orchestrator.ValidateAsync(Mapping<ColdPath>(provider), ddl, features, policy);

        // The shortfall is identical. What differs is that one application said it cannot work around it.
        optional.IsServiceable.Should().BeTrue("an index that cannot be built still leaves the reads correct");
        optional.State.Should().Be("Degraded");

        required.IsServiceable.Should().BeFalse();
        required.Corrective.Should().ContainSingle(finding => finding.Subject == "RequiredIndex")
            .Which.Detail.Should().Contain("ix_hot_lookup").And.Contain("sqlite");
    }

    [Fact]
    public void Functional_relational_module_is_the_single_orchestrator_owner()
    {
        var services = new ServiceCollection();
        var module = new RelationalModule();

        module.Register(services);
        module.Register(services);

        services.Count(x => x.ServiceType == typeof(IRelationalSchemaOrchestrator)).Should().Be(1);
    }

    [Fact]
    public void Cockroach_does_not_reference_or_activate_the_postgres_connector()
    {
        var references = typeof(CockroachAdapterFactory).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name);

        references.Should().NotContain("Koan.Data.Connector.Postgres");
        references.Should().Contain("Koan.Data.Relational.Npgsql");
    }

    [Fact]
    public async Task Disabled_ddl_cannot_add_columns_to_an_existing_table()
    {
        using var provider = Host(source => source.Map<OrderA>(map => map
            .Container("orders_a")
            .Key(order => order.Id).Name("ID")
            .Property(order => order.Reference).Name("REF")));
        var orchestrator = new RelationalSchemaOrchestrator();
        var ddl = new ProbeDdl(new RelationalTableShape(
            new Dictionary<string, RelationalColumnState?>(StringComparer.OrdinalIgnoreCase) { ["ID"] = null },
            ["ID"]));

        var action = () => orchestrator.EnsureCreatedAsync(
            Mapping<OrderA>(provider),
            ddl,
            new Features("sqlite"),
            new RelationalSchemaPolicy { DefaultSchema = "main", Ddl = RelationalDdlPolicy.NoDdl });

        await action.Should().ThrowAsync<InvalidOperationException>();
        ddl.Mutations.Should().Be(0);
    }

    [Fact]
    public async Task A_stale_projection_is_rebuilt_under_the_consent_that_would_have_created_it()
    {
        using var provider = Host(_ => { });
        var orchestrator = new RelationalSchemaOrchestrator();
        // The store holds the column and says it is not the one the mapping asked for — a generated column built
        // from an expression an older Koan wrote. Nothing about its type says so, which is why the store judges.
        var ddl = new ProbeDdl(DocumentShape(), mismatched: ["Rank"]);

        var validation = await orchestrator.EnsureCreatedAsync(
            Document<Reading>(provider),
            ddl,
            new Features("mysql", projects: true),
            new RelationalSchemaPolicy { DefaultSchema = "main", Ddl = RelationalDdlPolicy.AutoCreate });

        ddl.Rebuilds.Should().Equal("Rank");
        validation.State.Should().Be("Healthy", "the boot that noticed the drift is the boot that repaired it");
    }

    [Fact]
    public async Task A_stale_projection_is_reported_rather_than_rebuilt_without_ddl_consent()
    {
        using var provider = Host(_ => { });
        var orchestrator = new RelationalSchemaOrchestrator();
        var ddl = new ProbeDdl(DocumentShape(), mismatched: ["Rank"]);

        var validation = await orchestrator.EnsureCreatedAsync(
            Document<Reading>(provider),
            ddl,
            new Features("mysql", projects: true),
            new RelationalSchemaPolicy { DefaultSchema = "main", Ddl = RelationalDdlPolicy.NoDdl });

        // Repair is a mutation, so it needs the consent every other mutation needs. Without it the difference is
        // still named, and reads still resolve through the document.
        ddl.Rebuilds.Should().BeEmpty();
        ddl.Mutations.Should().Be(0);
        validation.State.Should().Be("Degraded");
        validation.Findings.Should().Contain(finding => finding.Subject == "Rank");
    }

    [Fact]
    public async Task Drift_on_a_column_that_holds_its_own_value_is_refused_rather_than_rebuilt()
    {
        using var provider = Host(_ => { });
        var orchestrator = new RelationalSchemaOrchestrator();
        // The structured root is where the entity lives. Rebuilding it would not recompute anything; it would
        // destroy the rows. Repair stops at columns the store derives.
        var ddl = new ProbeDdl(DocumentShape(), mismatched: ["Json"]);

        var action = () => orchestrator.EnsureCreatedAsync(
            Document<Reading>(provider),
            ddl,
            new Features("mysql", projects: true),
            new RelationalSchemaPolicy { DefaultSchema = "main", Ddl = RelationalDdlPolicy.AutoCreate });

        await action.Should().ThrowAsync<SchemaMismatchException>();
        ddl.Rebuilds.Should().BeEmpty();
    }

    /// <summary>The document shape every relational store compiles for an Entity: a key, the document, and the
    /// scalar columns the store projects out of it.</summary>
    private static MappingPlan Document<TEntity>(IServiceProvider provider) =>
        provider.GetRequiredService<IDataMappingPlans>().GetOrAdd<TEntity>(
            "Legacy", new MappingConvention(StorageAddress.From("main", "READING"), "Id", "Json"));

    private static RelationalTableShape DocumentShape() => new(
        new Dictionary<string, RelationalColumnState?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = new("Id", Nullable: false),
            ["Json"] = new("Json", Nullable: true),
            ["Rank"] = new("Rank", Nullable: true, IsProjected: true)
        },
        ["Id"]);

    private static MappingPlan Mapping<TEntity>(IServiceProvider provider)
        where TEntity : class =>
        provider.GetRequiredService<IDataMappingPlans>().Require<TEntity>("Legacy");

    private static ServiceProvider Host(Action<DataSourceBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddKoan(koan => configure(koan.Data.Source("Legacy")));
        return services.BuildServiceProvider();
    }

    public sealed class OrderA
    {
        public string Id { get; set; } = "a";
        public string Reference { get; set; } = "";
    }

    public sealed class OrderB
    {
        public string Id { get; set; } = "b";
    }

    public sealed class HotPath
    {
        public string Id { get; set; } = "h";

        [Index(Name = "ix_hot_lookup", Required = true)]
        public string Lookup { get; set; } = "";
    }

    public sealed class ColdPath
    {
        public string Id { get; set; } = "c";

        [Index(Name = "ix_cold_lookup")]
        public string Lookup { get; set; } = "";
    }

    public sealed class Reading
    {
        public string Id { get; set; } = "r";
        public int Rank { get; set; }
    }

    private sealed class Features(string provider, bool projects = false) : IRelationalStoreFeatures
    {
        public bool SupportsJsonFunctions => projects;
        public bool SupportsPersistedComputedColumns => projects;
        public string ProviderName => provider;
    }

    /// <summary>A store that reports one shape and records the grammar it was asked to speak, so the spec can
    /// assert the decision rather than a sample of its effects.</summary>
    private sealed class ProbeDdl(RelationalTableShape? shape = null, params string[] mismatched)
        : IRelationalDdlExecutor
    {
        private readonly HashSet<string> _mismatched = new(mismatched, StringComparer.Ordinal);
        private readonly List<string> _rebuilds = [];

        public int Mutations { get; private set; }

        /// <summary>Columns this store was asked to restate, in order.</summary>
        public IReadOnlyList<string> Rebuilds => _rebuilds;

        /// <summary>
        /// The store's own judgment. A named column is the one this store can see is stale — a recipe no type
        /// comparison could catch. Everything else keeps the default reading: compare CLR types where the store
        /// reported one, and accept the column where it did not.
        /// </summary>
        public bool ColumnMatches(RelationalColumnDefinition expected, RelationalColumnState actual)
            => !_mismatched.Contains(expected.Name) &&
               (actual.ClrType is null || expected.ClrType == actual.ClrType);

        public Task<RelationalTableShape?> Describe(RelationalTableDefinition table, CancellationToken ct = default)
            => Task.FromResult(shape);

        public Task RebuildProjection(
            RelationalTableDefinition table,
            RelationalColumnDefinition column,
            CancellationToken ct = default)
        {
            _rebuilds.Add(column.Name);
            // The column now computes what the mapping asked for, so the re-validation that follows sees it.
            _mismatched.Remove(column.Name);
            return Mutated();
        }

        public Task Create(RelationalTableDefinition table, CancellationToken ct = default) => Mutated();

        public Task AddColumn(RelationalTableDefinition table, RelationalColumnDefinition column, CancellationToken ct = default)
            => Mutated();

        private Task Mutated()
        {
            Mutations++;
            return Task.CompletedTask;
        }
    }
}
