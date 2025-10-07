using System.Text.Json.Nodes;
using Koan.Testing.Contracts;

namespace Koan.Testing.Fixtures;

public sealed class SeedPackFixture : IAsyncDisposable, IInitializableFixture
{
    private readonly string _packId;
    private JsonNode? _node;

    public SeedPackFixture(string packId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        _packId = packId;
    }

    public JsonNode Document => _node ?? throw new InvalidOperationException($"Seed pack '{_packId}' was not loaded.");

    public ValueTask InitializeAsync(TestContext context)
    {
        var path = SeedPackLocator.Resolve(_packId);
        using var stream = File.OpenRead(path);
        _node = JsonNode.Parse(stream) ?? throw new InvalidOperationException($"Seed pack '{_packId}' is empty.");
        context.Diagnostics.Debug("seed-pack.loaded", new { pack = _packId, path });
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _node = null;
        return ValueTask.CompletedTask;
    }
}
