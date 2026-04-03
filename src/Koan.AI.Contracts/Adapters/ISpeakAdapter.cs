using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Text-to-speech adapter (text → audio). Protocol category.
/// Providers: OpenedAI Speech, Coqui XTTS, Piper, ElevenLabs, OpenAI.
/// </summary>
public interface ISpeakAdapter : IAiAdapter
{
    /// <summary>Generate audio from text.</summary>
    Task<SpeakResponse> Speak(SpeakRequest request, CancellationToken ct = default);

    /// <summary>Stream audio chunks as they are generated. Returns null if not supported.</summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>>? StreamSpeak(
        SpeakRequest request, CancellationToken ct = default);
}
