using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Image editing adapter (image + instruction → image). Protocol category distinct from Imagine.
/// Providers: ComfyUI (inpainting), Stability AI, OpenAI, Google.
/// </summary>
public interface IEditAdapter : IAiAdapter
{
    /// <summary>Edit an image based on a text instruction.</summary>
    Task<EditResponse> Edit(EditRequest request, CancellationToken ct = default);
}
