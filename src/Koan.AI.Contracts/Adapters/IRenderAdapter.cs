using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Video generation adapter (text → video). Protocol category. Experimental.
/// Providers: ComfyUI (AnimateDiff), Runway, Kling, Google Veo.
/// </summary>
public interface IRenderAdapter : IAiAdapter
{
    /// <summary>Generate a video from a text prompt.</summary>
    Task<RenderResponse> Render(RenderRequest request, CancellationToken ct = default);
}
