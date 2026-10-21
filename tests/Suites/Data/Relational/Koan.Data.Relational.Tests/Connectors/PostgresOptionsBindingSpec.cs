using AwesomeAssertions;
using Xunit;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Postgres;
using Koan.Data.Connector.Postgres.Initialization;
using Koan.Data.Relational.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Relational.Tests.Connectors;

/// <summary>
/// TECHNICAL.md documents DdlPolicy, SchemaMatchingMode, AllowProductionDdl, NamingStyle and Separator under
/// Koan:Data:Postgres; the module bound no section, so every one of them was ignored (NK-031). Needs no server.
/// </summary>
public sealed class PostgresOptionsBindingSpec
{
    [Fact]
    public void Documented_keys_reach_the_options()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Koan:Data:Postgres:ConnectionString"] = "Host=db.example;Database=koan",
            ["Koan:Data:Postgres:DdlPolicy"] = "NoDdl",
            ["Koan:Data:Postgres:SchemaMatchingMode"] = "Strict",
            ["Koan:Data:Postgres:AllowProductionDdl"] = "true",
            ["Koan:Data:Postgres:NamingStyle"] = nameof(StorageNamingStyle.EntityType),
            ["Koan:Data:Postgres:Separator"] = "_",
        });

        options.DdlPolicy.Should().Be(RelationalDdlPolicy.NoDdl);
        options.SchemaMatching.Should().Be(RelationalSchemaMatchingMode.Strict);
        options.AllowProductionDdl.Should().BeTrue();
        options.NamingStyle.Should().Be(StorageNamingStyle.EntityType);
        options.Separator.Should().Be("_");
        options.ConnectionString.Should().Be("Host=db.example;Database=koan");
    }

    [Fact]
    public void The_property_spelling_of_schema_matching_is_read_too()
        => Resolve(new Dictionary<string, string?>
            {
                ["Koan:Data:Postgres:ConnectionString"] = "Host=db.example;Database=koan",
                ["Koan:Data:Postgres:SchemaMatching"] = "Strict",
            })
            .SchemaMatching.Should().Be(RelationalSchemaMatchingMode.Strict);

    [Fact]
    public void Unset_keys_keep_the_safe_defaults()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Koan:Data:Postgres:ConnectionString"] = "Host=db.example;Database=koan",
        });
        options.DdlPolicy.Should().Be(RelationalDdlPolicy.AutoCreate);
        options.SchemaMatching.Should().Be(RelationalSchemaMatchingMode.Relaxed);
        options.AllowProductionDdl.Should().BeFalse();
    }

    private static PostgresOptions Resolve(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddLogging();
        services.AddOptions();
        new PostgresModule().Register(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
    }
}
