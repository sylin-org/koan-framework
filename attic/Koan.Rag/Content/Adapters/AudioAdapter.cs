using Koan.AI.Contracts.Models;
using Koan.Rag.Abstractions;
using Koan.Rag.Content.Strategies;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Content.Adapters;

/// <summary>
/// Content adapter for audio files. Transcribes audio to text via
/// <c>Client.Transcribe()</c> then returns the transcript.
/// </summary>
[ContentAdapter(".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma")]
internal sealed class AudioAdapter : ContentAdapterBase
{
    public AudioAdapter(StrategyGenerator strategyGenerator, ILogger<AudioAdapter> logger)
        : base(strategyGenerator, logger) { }

    public override string Id => "audio";

    public override IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>([".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma"]);

    public override IReadOnlySet<Modality> SupportedModalities { get; } =
        new HashSet<Modality>([Modality.Audio]);

    public override async Task<ContentExtractionResult> Extract(
        ContentExtractionRequest request, CancellationToken ct = default)
    {
        // Transcribe audio to text
        var transcript = await Koan.AI.Client.Transcribe(request.Bytes, ct);

        if (string.IsNullOrWhiteSpace(transcript))
            return ContentExtractionResult.Empty;

        return new ContentExtractionResult
        {
            Text = transcript,
            Classification = new ContentClassification
            {
                Category = "audio/transcript",
                Description = "Audio content transcribed to text"
            },
            StrategyUsed = "transcribe",
            RoundsExecuted = 1
        };
    }
}
