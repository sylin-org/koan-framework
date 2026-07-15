using System.Collections.Generic;
using AwesomeAssertions;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Configuration;

/// <summary>
/// Connection placement contracts for the shared routed-source resolver. Explicit application configuration is
/// authoritative; the adapter's already-resolved autonomous connection is the fallback for an absent or
/// discovery-sentinel source.
/// </summary>
public sealed class AdapterConnectionResolverRoutingSpec
{
    private const string Provider = "Sqlite";
    private const string Discovered = "Data Source=.koan/data/Koan.sqlite";
    private const string Explicit = "Data Source=explicit.db";

    [Fact]
    public void Explicit_default_source_wins_over_the_discovered_default()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Default", Provider, Explicit, new Dictionary<string, string>()));

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            new ConfigurationBuilder().Build(), registry, Provider, "Default", Discovered,
            candidate => string.Equals(candidate, Provider, StringComparison.OrdinalIgnoreCase));

        resolved.Should().Be(Explicit);
    }

    [Fact]
    public void Provider_scoped_default_source_wins_over_the_discovered_default()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Koan:Data:Sources:Default:{Provider}:ConnectionString"] = Explicit
            })
            .Build();

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            config, new DataSourceRegistry(), Provider, "Default", Discovered);

        resolved.Should().Be(Explicit);
    }

    [Fact]
    public void Generic_default_owned_by_another_adapter_is_not_reused()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Default", "json", "data.json", new Dictionary<string, string>()));

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            new ConfigurationBuilder().Build(), registry, Provider, "Default", Discovered, OwnsProvider);

        resolved.Should().Be(Discovered);
    }

    [Fact]
    public void Generic_connection_string_owned_by_another_adapter_is_not_reused()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Archive", "json", "", new Dictionary<string, string>()));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Archive"] = "data.json",
                [$"Koan:Data:{Provider}:ConnectionString"] = Explicit
            })
            .Build();

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            config, registry, Provider, "Archive", Discovered, OwnsProvider);

        resolved.Should().Be(Explicit);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("postgresql")]
    [InlineData("npgsql")]
    public void Provider_aliases_own_the_generic_source_connection(string configuredAdapter)
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Archive", configuredAdapter, Explicit, new Dictionary<string, string>()));

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            new ConfigurationBuilder().Build(),
            registry,
            "Postgres",
            "Archive",
            Discovered,
            candidate => candidate.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
                         candidate.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
                         candidate.Equals("npgsql", StringComparison.OrdinalIgnoreCase));

        resolved.Should().Be(Explicit);
    }

    [Fact]
    public void Provider_scoped_default_remains_available_when_the_generic_source_belongs_elsewhere()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Default", "json", "data.json", new Dictionary<string, string>()));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Koan:Data:Sources:Default:{Provider}:ConnectionString"] = Explicit
            })
            .Build();

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            config, registry, Provider, "Default", Discovered,
            candidate => string.Equals(candidate, Provider, StringComparison.OrdinalIgnoreCase));

        resolved.Should().Be(Explicit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData(" AUTO ")]
    public void Unresolved_default_source_uses_the_discovered_default(string configured)
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Default", Provider, configured, new Dictionary<string, string>()));

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            new ConfigurationBuilder().Build(), registry, Provider, "Default", Discovered);

        resolved.Should().Be(Discovered);
    }

    [Fact]
    public void Unowned_generic_default_source_precedes_the_provider_default()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Koan:Data:{Provider}:ConnectionString"] = "Data Source=provider.db",
                ["ConnectionStrings:Default"] = "Data Source=generic.db"
            })
            .Build();

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            config, new DataSourceRegistry(), Provider, "Default", "Data Source=provider.db");

        resolved.Should().Be("Data Source=generic.db");
    }

    [Fact]
    public void Explicit_non_default_source_remains_authoritative()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Archive", Provider, Explicit, new Dictionary<string, string>()));

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            new ConfigurationBuilder().Build(), registry, Provider, "Archive", Discovered);

        resolved.Should().Be(Explicit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData(" AUTO ")]
    public void Unresolved_non_default_source_uses_the_resolved_default(string configured)
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Archive", Provider, configured, new Dictionary<string, string>()));

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            new ConfigurationBuilder().Build(), registry, Provider, "Archive", Discovered);

        resolved.Should().Be(Discovered);
    }

    [Fact]
    public void Foreign_generic_settings_are_ignored_but_provider_scoped_settings_remain_available()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Archive", "json", "data.json", new Dictionary<string, string>
            {
                ["Database"] = "17"
            }));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Koan:Data:Sources:Archive:{Provider}:Database"] = "4"
            })
            .Build();

        var resolved = AdapterConnectionResolver.GetSourceSetting(
            config, registry, Provider, "Archive", "Database", 0, OwnsProvider);

        resolved.Should().Be(4);
    }

    [Fact]
    public void Unresolved_default_without_a_discovery_result_preserves_the_sentinel()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Default", Provider, "auto", new Dictionary<string, string>()));

        var resolved = AdapterConnectionResolver.ResolveRoutedConnection(
            new ConfigurationBuilder().Build(), registry, Provider, "Default", null);

        resolved.Should().Be("auto");
    }

    [Fact]
    public void Missing_configuration_and_discovery_result_remains_fail_loud()
    {
        var act = () => AdapterConnectionResolver.ResolveRoutedConnection(
            new ConfigurationBuilder().Build(), new DataSourceRegistry(), Provider, "Default", null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No connection string found*");
    }

    private static bool OwnsProvider(string candidate)
        => string.Equals(candidate, Provider, StringComparison.OrdinalIgnoreCase);
}
