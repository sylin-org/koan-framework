using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Koan.Media.Web.Routing;

/// <summary>
/// Default <see cref="IOverlayResolver"/> backed by the app's
/// registered <see cref="IMediaSource"/> + <see cref="IMediaRecipeRegistry"/>.
///
/// <para>Overlay ids share the same id space as the host media — so a
/// brand logo uploaded as a regular MediaEntity can be referenced as an
/// overlay without separate registration. When the layer pins a recipe
/// (<see cref="MediaOverlaySource.RecipeName"/>), this resolver runs the
/// overlay's bytes through that recipe before returning, honoring the
/// MEDIA-0004 §7 depth-2 recursion cap.</para>
/// </summary>
public sealed class DefaultOverlayResolver : IOverlayResolver
{
    private readonly IMediaSource _source;
    private readonly IMediaRecipeRegistry _registry;
    private readonly ILogger<DefaultOverlayResolver> _logger;

    public DefaultOverlayResolver(
        IMediaSource source,
        IMediaRecipeRegistry registry,
        ILogger<DefaultOverlayResolver> logger)
    {
        _source = source;
        _registry = registry;
        _logger = logger;
    }

    public async Task<Stream?> OpenAsync(MediaOverlaySource source, int depth, CancellationToken ct)
    {
        if (depth > OverlayCompositor.MaxRecipeDepth)
        {
            _logger.LogWarning(
                "Overlay recipe nesting exceeded depth cap ({Cap}); rejecting recursive overlay '{Id}'.",
                OverlayCompositor.MaxRecipeDepth, source.MediaId);
            return null;
        }

        var handle = await _source.OpenAsync(source.MediaId, ct).ConfigureAwait(false);
        if (handle is null) return null;

        // No recipe → hand back the raw source bytes; compositor decodes.
        if (string.IsNullOrEmpty(source.RecipeName))
        {
            return handle.Bytes;
        }

        var recipe = _registry.Find(source.RecipeName);
        if (recipe is null)
        {
            _logger.LogWarning(
                "Overlay '{Id}' requested recipe '{Recipe}' but no such recipe is registered; returning raw bytes.",
                source.MediaId, source.RecipeName);
            return handle.Bytes;
        }

        // Apply the recipe and return the processed bytes as a fresh
        // MemoryStream so the compositor's Image.LoadAsync sees a regular
        // stream. The handle's source stream is disposed inside AsMedia().
        // Per MEDIA-0008 the recipe writes directly into the MemoryStream
        // we hand back to the compositor — no intermediate byte[].
        try
        {
            var buffer = new MemoryStream();
            await handle.Bytes.AsMedia(_logger)
                .Apply(recipe)
                .WriteToAsync(buffer, ct).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Overlay '{Id}' recipe '{Recipe}' application failed; layer will be skipped.",
                source.MediaId, source.RecipeName);
            return null;
        }
    }
}
