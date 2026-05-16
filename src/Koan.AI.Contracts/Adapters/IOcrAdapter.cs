using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// OCR (optical character recognition) adapter. Providers with dedicated OCR capabilities implement this interface.
/// When no dedicated IOcrAdapter is registered, the category router delegates OCR through <see cref="IChatAdapter"/>
/// using a multimodal vision request (Via delegation).
/// </summary>
public interface IOcrAdapter : IAiAdapter
{
    /// <summary>Extract text from an image.</summary>
    Task<OcrResponse> Recognize(OcrRequest request, CancellationToken ct = default);
}
