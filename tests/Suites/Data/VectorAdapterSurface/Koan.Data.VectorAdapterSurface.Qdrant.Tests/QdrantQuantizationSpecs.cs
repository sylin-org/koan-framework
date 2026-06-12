using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Vector;
using Koan.Data.Vector.Connector.Qdrant;
using Koan.Data.VectorAdapterSurface.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Qdrant.Tests;

/// <summary>
/// Adapter-specific specs that exercise <see cref="QuantizationOptions"/> beyond the
/// matrix kit's coverage. The kit verifies adapter-contract conformance (upsert/search/delete
/// round-trip); these specs verify that each quantization mode propagates correctly into
/// Qdrant's collection configuration AND that the contract still holds under each mode.
///
/// <para>
/// Each test builds an isolated <see cref="ServiceProvider"/> with custom options against the
/// shared container (provided via <see cref="IClassFixture{T}"/>). After exercising the
/// adapter, the test queries Qdrant's REST API directly to assert the collection-level config
/// reflects what was requested — proves the plumbing, not just the side effects.
/// </para>
/// </summary>
public class QdrantQuantizationSpecs : IClassFixture<QdrantTestFactory>, IAsyncLifetime
{
    private readonly QdrantTestFactory _factory;
    private HttpClient? _admin;

    public QdrantQuantizationSpecs(QdrantTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        if (!_factory.IsAvailable) return;
        Koan.Data.Core.AggregateConfigs.Reset();
        await _factory.ResetAsync();
        if (_factory.Endpoint is { } endpoint)
        {
            _admin = new HttpClient { BaseAddress = new Uri(endpoint) };
        }
    }

    public Task DisposeAsync()
    {
        _admin?.Dispose();
        _admin = null;
        return Task.CompletedTask;
    }

    private void SkipIfUnavailable()
        => Skip.If(!_factory.IsAvailable, $"[{nameof(QdrantTestFactory)}] {_factory.UnavailableReason ?? "Adapter infrastructure unavailable"}");

    private float[] Embed(string category, int seed) => EmbeddingFactory.ForCategory(category, seed, _factory.EmbeddingDimension);

    // ============================================================================================
    // Default (lean) profile — scalar quantization + on-disk originals
    // ============================================================================================

    [SkippableFact]
    public async Task DefaultProfile_isScalarQuantizationWithOnDiskOriginals()
    {
        SkipIfUnavailable();

        await using var sp = _factory.BuildServiceProviderWith(_ => { /* keep all adapter defaults */ });
        using var _ = AppHost.PushScope(sp);

        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));

        var config = await GetCollectionConfig();
        // Scalar quantization should be present under quantization_config.scalar.
        config.RootElement.GetProperty("result")
            .GetProperty("config")
            .GetProperty("quantization_config")
            .GetProperty("scalar")
            .GetProperty("type").GetString().Should().Be("int8");

        // Original vectors should live on disk per the lean profile.
        var vectorConfig = config.RootElement
            .GetProperty("result")
            .GetProperty("config")
            .GetProperty("params")
            .GetProperty("vectors");
        // Named-vector slot: walk into "default" (the configured VectorField).
        vectorConfig.GetProperty("default").GetProperty("on_disk").GetBoolean().Should().BeTrue(
            "the lean profile pairs scalar quantization with on_disk originals — that's where the 4× memory win materializes.");
    }

    // ============================================================================================
    // Per-mode plumbing — each request shape lands intact in Qdrant
    // ============================================================================================

    [SkippableFact]
    public async Task None_disablesQuantization_collectionHasNoQuantizationConfig()
    {
        SkipIfUnavailable();

        await using var sp = _factory.BuildServiceProviderWith(o =>
        {
            o.Quantization = new QuantizationOptions { Type = "None" };
            o.OnDisk = false; // full-fidelity in-memory profile
        });
        using var _ = AppHost.PushScope(sp);

        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));

        var config = await GetCollectionConfig();
        var configElement = config.RootElement.GetProperty("result").GetProperty("config");

        // Qdrant returns quantization_config as null (or omitted) when none is configured.
        if (configElement.TryGetProperty("quantization_config", out var quantConfig))
        {
            quantConfig.ValueKind.Should().Be(JsonValueKind.Null,
                "no quantization configured → Qdrant should return null/omitted quantization_config.");
        }

        // Vectors should NOT be on disk in this profile (we asked for full in-memory).
        configElement.GetProperty("params").GetProperty("vectors")
            .GetProperty("default").GetProperty("on_disk").GetBoolean().Should().BeFalse();
    }

    [SkippableFact]
    public async Task Scalar_explicitConfig_landsWithCorrectQuantileAndAlwaysRam()
    {
        SkipIfUnavailable();

        await using var sp = _factory.BuildServiceProviderWith(o =>
        {
            o.Quantization = new QuantizationOptions
            {
                Type = "Scalar",
                Quantile = 0.95,
                AlwaysRam = false
            };
        });
        using var _ = AppHost.PushScope(sp);

        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));

        var scalar = (await GetCollectionConfig()).RootElement
            .GetProperty("result")
            .GetProperty("config")
            .GetProperty("quantization_config")
            .GetProperty("scalar");

        scalar.GetProperty("type").GetString().Should().Be("int8");
        scalar.GetProperty("quantile").GetDouble().Should().BeApproximately(0.95, 0.001);
        scalar.GetProperty("always_ram").GetBoolean().Should().BeFalse();
    }

    [SkippableFact]
    public async Task Binary_quantization_landsAsBinaryBlock()
    {
        SkipIfUnavailable();

        await using var sp = _factory.BuildServiceProviderWith(o =>
        {
            o.Quantization = new QuantizationOptions
            {
                Type = "Binary",
                AlwaysRam = true,
                Oversampling = 4.0 // binary needs more aggressive oversampling to recover recall
            };
        });
        using var _ = AppHost.PushScope(sp);

        // Use a higher dimension proxy by upserting multiple items — binary quantization is
        // expressly designed for high-dim embeddings and may misbehave at dim=8, but the
        // collection-creation plumbing is what we're testing here, not recall fidelity.
        await Vector<TodoVector>.Save("v1", Embed("alpha", 1));

        var quantConfig = (await GetCollectionConfig()).RootElement
            .GetProperty("result")
            .GetProperty("config")
            .GetProperty("quantization_config");

        quantConfig.TryGetProperty("binary", out var binary).Should().BeTrue(
            "Type=Binary should produce a binary block in quantization_config.");
        binary.GetProperty("always_ram").GetBoolean().Should().BeTrue();
    }

    // ============================================================================================
    // Contract conformance — upsert/search round-trip under each mode
    // ============================================================================================

    [SkippableTheory]
    [InlineData("None")]
    [InlineData("Scalar")]
    [InlineData("Binary")]
    public async Task EachMode_upsertThenSearch_returnsTheUpsertedId(string quantizationType)
    {
        SkipIfUnavailable();

        await using var sp = _factory.BuildServiceProviderWith(o =>
        {
            o.Quantization = string.Equals(quantizationType, "None", StringComparison.OrdinalIgnoreCase)
                ? new QuantizationOptions { Type = "None" }
                : new QuantizationOptions { Type = quantizationType, Oversampling = 4.0 };
        });
        using var _ = AppHost.PushScope(sp);

        var embed = Embed("alpha", 1);
        await Vector<TodoVector>.Save("v1", embed);

        var hits = await Vector<TodoVector>.Search(embed, topK: 1);
        hits.Matches.Should().HaveCount(1, $"{quantizationType} quantization should still return the upserted point under exact-vector search.");
        hits.Matches[0].Id.Should().Be("v1");
    }

    [SkippableFact]
    public async Task UnknownQuantizationType_throwsAtCollectionCreate()
    {
        SkipIfUnavailable();

        await using var sp = _factory.BuildServiceProviderWith(o =>
        {
            o.Quantization = new QuantizationOptions { Type = "Bogus" };
        });
        using var _ = AppHost.PushScope(sp);

        var act = async () => await Vector<TodoVector>.Save("v1", Embed("alpha", 1));
        // The adapter validates the type string before sending to Qdrant — surface as
        // InvalidOperationException with a helpful message listing the valid values.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("Bogus") || e.Message.Contains("quantization"));
    }

    // ============================================================================================
    // Helpers
    // ============================================================================================

    private async Task<JsonDocument> GetCollectionConfig()
    {
        // Discover the collection name by listing collections — there'll be exactly one in the
        // freshly-reset Qdrant instance (the one we just upserted into via TodoVector).
        using var listResp = await _admin!.GetAsync("/collections");
        var listJson = await listResp.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listJson);
        var collectionName = listDoc.RootElement
            .GetProperty("result").GetProperty("collections")[0]
            .GetProperty("name").GetString()!;

        using var getResp = await _admin.GetAsync($"/collections/{collectionName}");
        var json = await getResp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }
}
