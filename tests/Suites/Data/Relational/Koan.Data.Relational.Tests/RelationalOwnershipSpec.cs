using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Connector.Cockroach;
using Koan.Data.Relational.Initialization;
using Koan.Data.Relational.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Relational.Tests;

public sealed class RelationalOwnershipSpec
{
    [Fact]
    public async Task Schema_policy_and_resolved_table_remain_request_local()
    {
        var orchestrator = new RelationalSchemaOrchestrator(new ServiceCollection().BuildServiceProvider());
        var ddl = new ProbeDdl();

        var strict = await orchestrator.ValidateAsync<ProbeEntity, string>(
            ddl,
            new Features("postgres"),
            "orders_a",
            new RelationalSchemaPolicy
            {
                DefaultSchema = "tenant_a",
                Matching = RelationalSchemaMatchingMode.Strict,
                Ddl = RelationalDdlPolicy.NoDdl
            });

        var relaxed = await orchestrator.ValidateAsync<ProbeEntity, string>(
            ddl,
            new Features("sqlite"),
            "orders_b",
            new RelationalSchemaPolicy
            {
                DefaultSchema = "main",
                Matching = RelationalSchemaMatchingMode.Relaxed,
                Ddl = RelationalDdlPolicy.AutoCreate
            });

        strict["Provider"].Should().Be("postgres");
        strict["Schema"].Should().Be("tenant_a");
        strict["Table"].Should().Be("orders_a");
        strict["State"].Should().Be("Unhealthy");

        relaxed["Provider"].Should().Be("sqlite");
        relaxed["Schema"].Should().Be("main");
        relaxed["Table"].Should().Be("orders_b");
        relaxed["State"].Should().Be("Degraded");
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
        var orchestrator = new RelationalSchemaOrchestrator(new ServiceCollection().BuildServiceProvider());
        var ddl = new ProbeDdl(tableExists: true);
        var policy = new RelationalSchemaPolicy
        {
            Projections = RelationalProjectionMode.PhysicalColumns,
            Ddl = RelationalDdlPolicy.NoDdl
        };

        var action = () => orchestrator.EnsureCreatedAsync<ProbeEntity, string>(
            ddl,
            new Features("sqlite"),
            "orders",
            policy);

        await action.Should().ThrowAsync<InvalidOperationException>();
        ddl.Mutations.Should().Be(0);
    }

    private sealed class ProbeEntity : IEntity<string>
    {
        public string Id { get; init; } = "probe";
        public string Name { get; init; } = "Probe";
    }

    private sealed class Features(string provider) : IRelationalStoreFeatures
    {
        public bool SupportsJsonFunctions => false;
        public bool SupportsPersistedComputedColumns => false;
        public bool SupportsIndexesOnComputedColumns => false;
        public string ProviderName => provider;
    }

    private sealed class ProbeDdl(bool tableExists = false) : IRelationalDdlExecutor
    {
        public int Mutations { get; private set; }
        public bool TableExists(string schema, string table) => tableExists;
        public bool ColumnExists(string schema, string table, string column)
            => tableExists && column is "Id" or "Json";
        public void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json") => Mutations++;
        public void CreateTableWithColumns(string schema, string table, IReadOnlyList<RelationalColumnDefinition> columns) => Mutations++;
        public void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted) => Mutations++;
        public void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable) => Mutations++;
        public void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique) => Mutations++;
    }
}
