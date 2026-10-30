using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

/// <summary>
/// DuckDB declares conditional replacement, so a guard on a stored member replaces the row only while the stored row
/// still matches it, and refuses once it no longer does.
/// </summary>
public sealed class DuckDbConditionalReplaceSpec
{
    [Fact]
    public async Task A_guard_on_a_stored_member_replaces_the_matching_row_and_refuses_a_changed_one()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"koan-duckdb-cas-{Guid.CreateVersion7():n}");
        Directory.CreateDirectory(folder);
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "duckdb")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={Path.Combine(folder, "main.duckdb")}")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);
        var row = await new Revisioned { Revision = "first", Text = "original" }.Save();

        var replaced = await new Revisioned { Id = row.Id, Revision = "second", Text = "replaced" }
            .ReplaceIf(stored => stored.Revision == "first");
        var stale = await new Revisioned { Id = row.Id, Revision = "third", Text = "stale" }
            .ReplaceIf(stored => stored.Revision == "first");

        replaced.Should().BeTrue();
        stale.Should().BeFalse();
        var stored = await Revisioned.Get(row.Id);
        stored!.Revision.Should().Be("second");
        stored.Text.Should().Be("replaced");
    }

    private sealed class Revisioned : Entity<Revisioned>
    {
        public string Revision { get; set; } = "";
        public string Text { get; set; } = "";
    }
}
