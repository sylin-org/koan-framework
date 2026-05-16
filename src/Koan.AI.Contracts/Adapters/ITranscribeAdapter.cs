using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Speech-to-text adapter (audio → text). Protocol category.
/// Providers: whisper.cpp, Speaches, Deepgram, AssemblyAI, OpenAI.
/// </summary>
public interface ITranscribeAdapter : IAiAdapter
{
    /// <summary>Transcribe audio to text.</summary>
    Task<TranscribeResponse> Transcribe(TranscribeRequest request, CancellationToken ct = default);

    /// <summary>Stream transcription segments from a live audio source. Returns null if not supported.</summary>
    IAsyncEnumerable<TranscribeSegment>? StreamTranscribe(
        Stream audioStream, TranscribeRequest request, CancellationToken ct = default);
}
