using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Cutover.Options;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Koan.Data.Cutover.Tests;

/// <summary>
/// NK-305, DATA-0148: cutover's physical-distinctness check asks the stores' markers, like Move. A target whose
/// connection spells the active database another way reads the active default's marker and is refused; the markers
/// never count as containers in either inventory.
/// </summary>
public sealed class CutoverStoreMarkerSpec
{
    private const string Target = "Spelled";

    [Fact]
    public async Task A_target_that_spells_the_active_database_another_way_is_refused_by_its_store_marker()
    {
        var paths = Paths.Create();
        try
        {
            await using var host = await Boot(paths, OtherSpelling(paths.Source));
            AppHost.Current = host.Services;

            var plan = await Koan.Data.Core.Data.Source(Target).PromoteToDefault()
                .Plan(TestContext.Current.CancellationToken);

            plan.Source.ConnectionIdentity.Should().NotBe(plan.Target.ConnectionIdentity, "the spellings differ");
            plan.Blockers.Should().Contain(blocker =>
                blocker.Code == "koan.data.cutover.target-policy" && blocker.Reason.Contains("store marker"));
        }
        finally
        {
            Reset(paths);
        }
    }

    [Fact]
    public async Task Planning_writes_each_store_marker_and_neither_inventory_counts_it()
    {
        var paths = Paths.Create();
        try
        {
            await using var host = await Boot(paths, paths.Target);
            AppHost.Current = host.Services;
            await new CutoverRecord { Id = "0001", Value = "source" }.Save(TestContext.Current.CancellationToken);

            var plan = await Koan.Data.Core.Data.Source(Target).PromoteToDefault()
                .Plan(TestContext.Current.CancellationToken);

            plan.CanRun.Should().BeTrue();
            (await Tables(paths.Source)).Should().Contain(StoreMarker.Name);
            (await Tables(paths.Target)).Should().Contain(StoreMarker.Name);
        }
        finally
        {
            Reset(paths);
        }
    }

    private static async Task<List<string>> Tables(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table'";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken)) names.Add(reader.GetString(0));
        return names;
    }

    private static Task<IntegrationHost> Boot(Paths paths, string target) => KoanIntegrationHost.Configure()
        .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={paths.Source};Pooling=False",
            [$"Koan:Data:Sources:{Target}:Adapter"] = "sqlite",
            [$"Koan:Data:Sources:{Target}:ConnectionString"] = $"Data Source={target};Pooling=False",
            ["Koan:Data:Route:StatePath"] = paths.State,
            ["Koan:Data:Cutover:WriterOwnership"] =
                CutoverWriterOwnership.HostExclusiveOrExternallyQuiesced.ToString()
        })
        .ConfigureServices(static services => services.AddKoan())
        .StartAsync(TestContext.Current.CancellationToken);

    private static string OtherSpelling(string path)
    {
        if (OperatingSystem.IsWindows()) return @"\\?\" + path;
        var link = Path.GetDirectoryName(path) + "-link";
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(path)!);
        return Path.Combine(link, Path.GetFileName(path));
    }

    private static void Reset(Paths paths)
    {
        AppHost.Current = null;
        TestHooks.ResetDataConfigs();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(paths.Root, recursive: true); }
        catch { }
    }

    private sealed record Paths(string Root, string Source, string Target, string State)
    {
        internal static Paths Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "koan-cutover-marker", Guid.CreateVersion7().ToString("n"));
            Directory.CreateDirectory(root);
            return new Paths(
                root,
                Path.Combine(root, "source.db"),
                Path.Combine(root, "target.db"),
                Path.Combine(root, "control", "active-route.json"));
        }
    }
}
