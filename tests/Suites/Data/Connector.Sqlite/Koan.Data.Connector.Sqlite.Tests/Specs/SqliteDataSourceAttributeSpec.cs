using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Composition;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-038 (DATA-0127). <c>[DataSource(name)]</c> routes the entity to that configured source. Before, the attribute
/// was read by nothing and a decorated entity was stored in the default database.
/// </summary>
[Collection(nameof(SqliteDataSourceAttributeSpec))]
[CollectionDefinition(nameof(SqliteDataSourceAttributeSpec), DisableParallelization = true)]
public sealed class SqliteDataSourceAttributeSpec
{
    [DataSource("Archive")]
    public sealed class ArchivedNote : Entity<ArchivedNote>
    {
        public string Text { get; set; } = "";
    }

    [DataSource("Vault")]
    public sealed class VaultNote : Entity<VaultNote>
    {
        public string Text { get; set; } = "";
    }

    [Fact]
    public async Task A_decorated_entity_is_stored_in_its_declared_source()
    {
        await using var host = await Start();

        var saved = await new ArchivedNote { Text = "kept" }.Save();

        (await ArchivedNote.Get(saved.Id)).Should().NotBeNull();
        Tables(host.Archive).Should().Contain(table => table.Contains(nameof(ArchivedNote)));
        Tables(host.Primary).Should().NotContain(table => table.Contains(nameof(ArchivedNote)),
            "the declared source, not the default database, holds the entity");
    }

    [Fact]
    public async Task An_explicit_source_context_overrides_the_declaration()
    {
        await using var host = await Start();

        using (EntityContext.Source("Default"))
            await new ArchivedNote { Text = "default" }.Save();

        Tables(host.Primary).Should().Contain(table => table.Contains(nameof(ArchivedNote)));
    }

    [Fact]
    public async Task An_unconfigured_declared_source_is_refused()
    {
        await using var host = await Start();

        var refusal = await Record.ExceptionAsync(() => new VaultNote { Text = "x" }.Save());

        refusal.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("[DataSource(\"Vault\")]").And.Contain("Koan:Data:Sources:Vault");
        Tables(host.Primary).Should().NotContain(table => table.Contains(nameof(VaultNote)));
    }

    [Fact]
    public async Task A_decorated_root_is_outside_default_route_promotion()
    {
        await using var host = await Start();

        host.Services.GetRequiredService<DataApplicationManifest>().Roots
            .Single(root => root.RootType == typeof(ArchivedNote))
            .RouteScope.Should().Be(DataEntityRouteScope.OutsideDefault);
    }

    private static IReadOnlyList<string> Tables(string file)
    {
        if (!File.Exists(file)) return [];
        using var connection = new SqliteConnection($"Data Source={file};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<AttributeHost> Start()
    {
        var primary = Path.Combine(Path.GetTempPath(), $"koan-sqlite-ds-primary-{Guid.CreateVersion7():n}.db");
        var archive = Path.Combine(Path.GetTempPath(), $"koan-sqlite-ds-archive-{Guid.CreateVersion7():n}.db");
        var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={primary}")
            .WithSetting("Koan:Data:Sources:Archive:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Archive:ConnectionString", $"Data Source={archive}")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();
        return new AttributeHost(host, primary, archive);
    }

    private sealed class AttributeHost(IntegrationHost host, string primary, string archive) : IAsyncDisposable
    {
        public IServiceProvider Services => host.Services;
        public string Primary => primary;
        public string Archive => archive;

        public async ValueTask DisposeAsync()
        {
            await host.DisposeAsync();
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { primary, archive })
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
