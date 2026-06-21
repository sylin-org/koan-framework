using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.Core.Hosting.App;

namespace GardenCoopEmbedded;

/// <summary>
/// Embeds text in-process by calling the registered ONNX <see cref="IEmbedAdapter"/> directly.
/// </summary>
/// <remarks>
/// The higher-level <c>Koan.AI.Client.Embed</c> facade routes through the AI <i>source</i> registry, which the
/// local ONNX adapter does not yet populate (it registers as an adapter, not a source) — so it isn't routable
/// through the facade or the <c>[Embedding]</c> worker yet. That's a framework follow-up; calling the adapter
/// directly keeps this sample self-contained and reliable in the meantime. See the README.
/// </remarks>
internal static class Embedder
{
    public static async Task<float[]> Embed(string text, CancellationToken ct = default)
    {
        var registry = AppHost.Current?.GetService(typeof(IAiAdapterRegistry)) as IAiAdapterRegistry
            ?? throw new InvalidOperationException("AI adapter registry unavailable.");
        var embed = registry.All.OfType<IEmbedAdapter>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No in-process embedding adapter registered. Set Koan:Ai:Onnx:ModelPath to a local ONNX model.");
        var response = await embed.Embed(new AiEmbeddingsRequest { Input = { text } }, ct).ConfigureAwait(false);
        return response.Vectors[0];
    }
}
