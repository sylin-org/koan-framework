using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Abstractions;
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

    private sealed class Features(string provider) : IRelationalStoreFeatures
    {
        public bool SupportsJsonFunctions => false;
        public bool SupportsPersistedComputedColumns => false;
        public string ProviderName => provider;
    }

    /// <summary>A store that reports one shape and records the grammar it was asked to speak, so the spec can
    /// assert the decision rather than a sample of its effects.</summary>
    private sealed class ProbeDdl(RelationalTableShape? shape = null) : IRelationalDdlExecutor
    {
        public int Mutations { get; private set; }

        public Task<RelationalTableShape?> Describe(RelationalTableDefinition table, CancellationToken ct = default)
            => Task.FromResult(shape);

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
