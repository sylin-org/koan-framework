using Koan.Core.Capabilities;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Koan.Data.VectorAdapterSurface.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.SqliteVec.Tests;

public sealed class SqliteVecReferenceSpecs(SqliteVecTestFactory factory) : IAsyncLifetime
{
    private IDisposable? _scope;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAsync();
        _scope = AppHost.PushScope(factory.Services);
    }

    public ValueTask DisposeAsync()
    {
        _scope?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Euclidean_space_uses_native_l2_and_portable_similarity()
    {
        await Vector<SqliteVecEuclideanDoc>.Save("same", Point(0, 0));
        await Vector<SqliteVecEuclideanDoc>.Save("five-away", Point(3, 4));

        var result = await Vector<SqliteVecEuclideanDoc>.Search(Point(0, 0), query => query.Top(2));

        Assert.Equal(["same", "five-away"], result.Items.Select(item => item.Id));
        Assert.Equal(1d, result.Items[0].Similarity, 10);
        Assert.Equal(1d / 6d, result.Items[1].Similarity, 5);
        Assert.Equal(VectorMetric.Euclidean, result.Execution.Metric);
        Assert.Equal(VectorSearchAccuracy.Exact, result.Execution.Accuracy);
    }

    [Fact]
    public async Task Dot_product_space_is_declined_before_source_mutation()
    {
        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            Vector<SqliteVecDotProductDoc>.Save("unsupported", Point(1, 0)));
        Assert.Contains("not DotProduct", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Neutral_metadata_round_trips_declared_scalar_kinds()
    {
        var now = new DateTime(2026, 7, 28, 12, 34, 56, DateTimeKind.Utc);
        var offset = new DateTimeOffset(2026, 7, 28, 12, 34, 56, TimeSpan.FromHours(-4));
        var metadata = new DataObject([
            new DataProperty("I8", (sbyte)-8),
            new DataProperty("U8", (byte)8),
            new DataProperty("I16", (short)-16),
            new DataProperty("U16", (ushort)16),
            new DataProperty("I32", -32),
            new DataProperty("U32", 32u),
            new DataProperty("I64", -64L),
            new DataProperty("U64", 64UL),
            new DataProperty("F32", 1.25f),
            new DataProperty("F64", 2.5d),
            new DataProperty("Decimal", 3.75m),
            new DataProperty("Guid", Guid.Parse("12345678-1234-5678-9abc-def012345678")),
            new DataProperty("Date", new DateOnly(2026, 7, 28)),
            new DataProperty("Time", new TimeOnly(12, 34, 56)),
            new DataProperty("DateTime", now),
            new DataProperty("Offset", offset),
            new DataProperty("Duration", TimeSpan.FromMinutes(90)),
            new DataProperty("Bytes", new byte[] { 1, 2, 3 })
        ]);

        await Vector<SqliteVecMetadataDoc>.Save("types", Point(1, 0), metadata);
        var stored = Assert.IsType<DataObject>((await Vector<SqliteVecMetadataDoc>.Get("types"))!.Metadata);
        var values = stored.Properties.ToDictionary(property => property.Name, property => property.Value);

        Assert.IsType<sbyte>(values["I8"]); Assert.IsType<byte>(values["U8"]);
        Assert.IsType<short>(values["I16"]); Assert.IsType<ushort>(values["U16"]);
        Assert.IsType<int>(values["I32"]); Assert.IsType<uint>(values["U32"]);
        Assert.IsType<long>(values["I64"]); Assert.IsType<ulong>(values["U64"]);
        Assert.IsType<float>(values["F32"]); Assert.IsType<double>(values["F64"]);
        Assert.IsType<decimal>(values["Decimal"]); Assert.IsType<Guid>(values["Guid"]);
        Assert.IsType<DateOnly>(values["Date"]); Assert.IsType<TimeOnly>(values["Time"]);
        Assert.Equal(now, Assert.IsType<DateTime>(values["DateTime"]));
        Assert.Equal(offset, Assert.IsType<DateTimeOffset>(values["Offset"]));
        Assert.IsType<TimeSpan>(values["Duration"]);
        Assert.Equal([1, 2, 3], Assert.IsType<byte[]>(values["Bytes"]));
    }

    [Fact]
    public async Task File_backed_space_survives_host_restart()
    {
        await Vector<SqliteVecDurableDoc>.Save("durable", Point(1, 0), new { Version = 1 });
        _scope?.Dispose();
        _scope = null;

        await factory.RestartPreservingStoreAsync();
        _scope = AppHost.PushScope(factory.Services);

        var point = await Vector<SqliteVecDurableDoc>.Get("durable");
        Assert.NotNull(point);
        Assert.Equal(Point(1, 0), point.Embedding.ToArray());
    }

    [Fact]
    public void Capabilities_describe_only_native_guarantees()
    {
        var capabilities = Vector<SqliteVecEuclideanDoc>.GetCapabilities();
        Assert.True(capabilities.Has(VectorCaps.Knn));
        Assert.True(capabilities.Has(VectorCaps.AtomicBatch));
        Assert.True(capabilities.Has(VectorCaps.ScopeIsolation));
        Assert.False(capabilities.Has(VectorCaps.Filters));
        Assert.False(capabilities.Has(VectorCaps.Hybrid));
        Assert.False(capabilities.Has(VectorCaps.NativeContinuation));
    }

    [Fact]
    public async Task Candidate_bound_rejects_before_native_execution()
    {
        var repository = factory.Services.GetRequiredService<IVectorService>()
            .TryGetRepository<SqliteVecEuclideanDoc, string>()
            ?? throw new InvalidOperationException("SqliteVec repository did not resolve.");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.Search(
                new VectorSearchRequest(Point(1, 0), 100_000, Space: "euclidean"),
                VectorScope.Unscoped));
        Assert.Contains("stable cutoff tie", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_self_match_stays_within_the_distance_guard()
    {
        // The find-similar grammar re-embeds an entity and searches with its own vector, so the store
        // must tolerate an exact self-match. sqlite-vec computes cosine distance as 1 - cos in f32;
        // accumulation drift can push that a hair below zero for incommensurate components.
        var vectors = new[]
        {
            new float[] { 0.1f, 0.2f, 0.3f, 0.7f, 0.11f, 0.13f, 0.17f, 0.19f },
            new float[] { 100.1f, 100.2f, 0.3f, 0.7f, 1.1f, 1.3f, 1.7f, 1.9f },
            new float[] { 0.1234567f, 7.654321f, 123.456f, 0.007f, 31.4f, 2.71f, 0.577f, 1.618f }
        };

        for (var index = 0; index < vectors.Length; index++)
        {
            var id = $"self-{index}";
            await Vector<TodoVector>.Save(id, vectors[index]);
            var stored = (await Vector<TodoVector>.Get(id))!.Embedding.ToArray();

            var result = await Vector<TodoVector>.Search(stored, query => query.Top(1));

            Assert.Equal(id, result.Items[0].Id);
            Assert.Equal(1d, result.Items[0].Similarity, 6);
        }
    }

    private static float[] Point(float x, float y) => [x, y, 0f, 0f, 0f, 0f, 0f, 0f];
}

public sealed class SqliteVecEuclideanDoc : Koan.Data.Core.Model.Entity<SqliteVecEuclideanDoc>;
public sealed class SqliteVecDotProductDoc : Koan.Data.Core.Model.Entity<SqliteVecDotProductDoc>;
public sealed class SqliteVecDurableDoc : Koan.Data.Core.Model.Entity<SqliteVecDurableDoc>;
public sealed class SqliteVecMetadataDoc : Koan.Data.Core.Model.Entity<SqliteVecMetadataDoc>;
