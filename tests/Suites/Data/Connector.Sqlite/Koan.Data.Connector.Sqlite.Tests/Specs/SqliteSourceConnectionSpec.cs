using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-010 / NK-036 (DATA-0126). A named source that declares an adapter and no connection of its own used to read and
/// write the provider's default database under the same table names, so its rows mixed with Default's. It is now
/// refused; sharing the default connection is the explicit <c>ConnectionString: auto</c>.
/// </summary>
[Collection(nameof(SqliteSourceConnectionSpec))]
[CollectionDefinition(nameof(SqliteSourceConnectionSpec), DisableParallelization = true)]
public sealed class SqliteSourceConnectionSpec
{
    public sealed class Draft : Entity<Draft>
    {
        public string Title { get; set; } = "";
    }

    [Fact]
    public async Task A_source_without_its_own_connection_is_refused_and_the_default_store_is_untouched()
    {
        await using var host = await Start(previewConnection: null);

        Exception? refusal;
        using (EntityContext.Source("Preview"))
            refusal = await Record.ExceptionAsync(() => new Draft { Title = "draft" }.Save());

        refusal.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("source 'Preview'")
            .And.Contain("Koan:Data:Sources:Preview:ConnectionString");
        (await Draft.All()).Should().BeEmpty("a refused Preview write must not land in the Default store");
    }

    [Fact]
    public async Task A_source_that_declares_auto_shares_the_default_connection()
    {
        await using var host = await Start(previewConnection: "auto");

        Draft saved;
        using (EntityContext.Source("Preview"))
            saved = await new Draft { Title = "shared" }.Save();

        (await Draft.Get(saved.Id)).Should().NotBeNull("auto is the declared intent to share the default connection");
    }

    private static async Task<SourceHost> Start(string? previewConnection)
    {
        var primary = Path.Combine(Path.GetTempPath(), $"koan-sqlite-source-{Guid.CreateVersion7():n}.db");
        var configure = KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={primary}")
            .WithSetting("Koan:Data:Sources:Preview:Adapter", "sqlite");
        if (previewConnection is not null)
            configure = configure.WithSetting("Koan:Data:Sources:Preview:ConnectionString", previewConnection);
        var host = await configure.ConfigureServices(services => services.AddKoan()).StartAsync();
        return new SourceHost(host, primary);
    }

    private sealed class SourceHost(IAsyncDisposable host, string file) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await host.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
