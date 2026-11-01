using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// DATA-0166: a vector store cannot join a native transaction, so a vector write inside a hold would run at once and
/// survive its rollback. It refuses, through <c>Vector&lt;T&gt;</c> and through the repository alike, and a vector read goes on.
/// </summary>
public sealed class HeldVectorSpec : IAsyncLifetime
{
    [VectorAdapter("inmemory")]
    public sealed class HeldVectorDoc : Entity<HeldVectorDoc>
    {
        public string Title { get; set; } = "";
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"koan-vector-hold-{Guid.CreateVersion7():n}.db");
    private IntegrationHost? _host;

    private static readonly float[] Embedding = [1, 0, 0, 0, 0, 0, 0, 0];

    public async ValueTask InitializeAsync()
    {
        _host = await KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={_path}",
            })
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("Default").Vector<HeldVectorDoc>(space => space.Name("held-vector").Dimensions(8))))
            .StartAsync(TestContext.Current.CancellationToken);
        AppHost.Current = _host.Services;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            if (ReferenceEquals(AppHost.Current, _host.Services)) AppHost.Current = null;
            await _host.DisposeAsync();
        }
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }

    [Fact]
    public async Task Every_vector_write_refuses_inside_a_hold_and_nothing_reaches_the_vector_store()
    {
        await Vector<HeldVectorDoc>.Save("kept", Embedding);
        var repository = _host!.Services.GetRequiredService<IVectorService>().TryGetRepository<HeldVectorDoc, string>()!;

        using (await EntityContext.Hold("vectors"))
        {
            (await Vector<HeldVectorDoc>.Get("kept")).Should().NotBeNull("a vector read goes on");

            var writes = new Func<Task>[]
            {
                () => Vector<HeldVectorDoc>.Save("written", Embedding),
                () => Vector<HeldVectorDoc>.Save(new[] { ("written", Embedding, (object?)null) }),
                () => Vector<HeldVectorDoc>.Delete("kept"),
                () => Vector<HeldVectorDoc>.Delete(new[] { "kept" }),
                () => Vector<HeldVectorDoc>.Clear(),
                () => repository.Upsert("written", Embedding),
                () => repository.UpsertMany(new[] { ("written", Embedding, (object?)null) }),
                () => repository.Delete("kept"),
                () => repository.DeleteMany(new[] { "kept" }),
            };
            foreach (var write in writes)
                (await write.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("cannot join");

            await EntityContext.Rollback();
        }

        (await Vector<HeldVectorDoc>.Get("written")).Should().BeNull();
        (await Vector<HeldVectorDoc>.Get("kept")).Should().NotBeNull();

        await Vector<HeldVectorDoc>.Save("after the hold", Embedding);
        (await Vector<HeldVectorDoc>.Get("after the hold")).Should().NotBeNull("outside a hold it writes as it always did");
    }

    [Fact]
    public async Task A_vector_write_after_the_hold_has_committed_goes_on_inside_its_block()
    {
        using (await EntityContext.Hold("vectors"))
        {
            await EntityContext.Commit();
            await Vector<HeldVectorDoc>.Save("after commit", Embedding);
        }
        (await Vector<HeldVectorDoc>.Get("after commit")).Should().NotBeNull();
    }
}
