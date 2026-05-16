using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Image generation adapter (text → image). Protocol category with distinct I/O from Chat.
/// Providers: ComfyUI, Stability AI, OpenAI (GPT-image), Google (Gemini).
/// </summary>
public interface IImagineAdapter : IAiAdapter
{
    /// <summary>Generate an image from a text prompt.</summary>
    Task<ImagineResponse> Imagine(ImagineRequest request, CancellationToken ct = default);
}
